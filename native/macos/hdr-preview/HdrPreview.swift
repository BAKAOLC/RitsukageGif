import AppKit
import Metal
import QuartzCore

private typealias ChangeCallback = @convention(c) (UnsafeMutableRawPointer?) -> Void

private final class HdrView: NSView {
    let metalLayer = CAMetalLayer()
    let device: MTLDevice
    let queue: MTLCommandQueue
    let pipeline: MTLRenderPipelineState
    var texture: MTLTexture?
    var patch: MTLTexture?
    var patchRect = SIMD4<Float>(repeating: 0)
    var selection = SIMD4<Float>(repeating: 0)
    var patchAlpha: Float = 2
    let inFlight = DispatchSemaphore(value: 3)
    let renderQueue = DispatchQueue(label: "RitsukageGif.HdrPreview.Metal", qos: .userInteractive)
    var pendingFrames = 0
    var waiting = false
    var failed = false
    var alphaMode: Float = 2
    var white: Float = 1
    var smoothScaling: Float = 1
    var callback: ChangeCallback?
    var context: UnsafeMutableRawPointer?
    var observer: NSObjectProtocol?
    var windowObserver: NSObjectProtocol?

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    init?(libraryPath: String) {
        guard let device = MTLCreateSystemDefaultDevice(), let queue = device.makeCommandQueue() else { return nil }
        self.device = device
        self.queue = queue
        do {
            let library = try device.makeLibrary(URL: URL(fileURLWithPath: libraryPath))
            let descriptor = MTLRenderPipelineDescriptor()
            descriptor.vertexFunction = library.makeFunction(name: "vertexMain")
            descriptor.fragmentFunction = library.makeFunction(name: "fragmentMain")
            descriptor.colorAttachments[0].pixelFormat = .rgba16Float
            pipeline = try device.makeRenderPipelineState(descriptor: descriptor)
        } catch { return nil }
        super.init(frame: NSRect(x: 0,y: 0,width: 1,height: 1))
        wantsLayer = true
        metalLayer.device = device
        metalLayer.pixelFormat = .rgba16Float
        metalLayer.colorspace = CGColorSpace(name: CGColorSpace.extendedLinearSRGB)
        metalLayer.wantsExtendedDynamicRangeContent = true
        metalLayer.isOpaque = false
        metalLayer.framebufferOnly = true
        layer = metalLayer
        observer = NotificationCenter.default.addObserver(
            forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main
        ) { [weak self] _ in
            guard let self else { return }
            self.updateSize()
            self.callback?(self.context)
        }
    }
    required init?(coder: NSCoder) { return nil }
    deinit {
        if let observer { NotificationCenter.default.removeObserver(observer) }
        if let windowObserver { NotificationCenter.default.removeObserver(windowObserver) }
    }
    override func layout() { super.layout(); updateSize(); drawFrame() }
    override func setFrameSize(_ newSize: NSSize) {
        super.setFrameSize(newSize); updateSize(); drawFrame()
    }
    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        if let windowObserver { NotificationCenter.default.removeObserver(windowObserver) }
        windowObserver = nil
        if let window {
            windowObserver = NotificationCenter.default.addObserver(
                forName: NSWindow.didChangeScreenNotification, object: window, queue: .main
            ) { [weak self] _ in
                guard let self else { return }; self.updateSize(); self.callback?(self.context)
            }
        }
        updateSize(); callback?(context)
    }
    override func viewDidChangeBackingProperties() {
        super.viewDidChangeBackingProperties(); updateSize(); drawFrame(); callback?(context)
    }
    private func updateSize() {
        let scale = window?.backingScaleFactor ?? 1
        metalLayer.contentsScale = scale
        metalLayer.drawableSize = CGSize(width: max(1,bounds.width * scale),height: max(1,bounds.height * scale))
    }
    func upload(_ bytes: UnsafeRawPointer, _ width: Int, _ height: Int, _ stride: Int, _ white: Float,
                _ alphaMode: Int32, _ smoothScaling: Int32) -> Int32 {
        if failed { return -5 }
        guard inFlight.wait(timeout: .now()) == .success else { waiting = true; return 1 }
        if texture?.width != width || texture?.height != height ||
            pendingFrames != 0 {
            let descriptor = MTLTextureDescriptor.texture2DDescriptor(
                pixelFormat: .rgba16Float,width: width,height: height,mipmapped: false)
            descriptor.usage = .shaderRead
            descriptor.storageMode = .managed
            // Apple Silicon uses shared memory; managed textures are only available on discrete macOS GPUs.
            if device.hasUnifiedMemory { descriptor.storageMode = .shared }
            texture = device.makeTexture(descriptor: descriptor)
        }
        guard let texture else { inFlight.signal(); return -3 }
        texture.replace(region: MTLRegionMake2D(0,0,width,height),mipmapLevel: 0,
                        withBytes: bytes,bytesPerRow: stride)
        self.white = white
        self.alphaMode = Float(alphaMode)
        self.smoothScaling = smoothScaling != 0 ? 1 : 0
        updateSize()
        return drawFrame(reserved: true)
    }
    @discardableResult func drawFrame(reserved: Bool = false) -> Int32 {
        guard let texture else { if reserved { inFlight.signal() }; return 0 }
        if !reserved && inFlight.wait(timeout: .now()) != .success { waiting = true; return 1 }
        pendingFrames += 1
        let layer = metalLayer, commandQueue = queue, state = pipeline
        let patch = self.patch ?? texture
        let mode = SIMD4<Float>(self.white,self.alphaMode,self.smoothScaling,0)
        let rect = patchRect, selected = selection, patchMode = patchAlpha
        let semaphore = inFlight
        renderQueue.async { [weak self] in
            autoreleasepool {
                guard let drawable = layer.nextDrawable(), let command = commandQueue.makeCommandBuffer() else {
                    semaphore.signal()
                    DispatchQueue.main.async { [weak self] in self?.finishFrame(failed: true) }
                    return
                }
                let pass = MTLRenderPassDescriptor()
                pass.colorAttachments[0].texture = drawable.texture
                pass.colorAttachments[0].loadAction = .clear
                pass.colorAttachments[0].storeAction = .store
                pass.colorAttachments[0].clearColor = MTLClearColorMake(0,0,0,0)
                guard let encoder = command.makeRenderCommandEncoder(descriptor: pass) else {
                    semaphore.signal()
                    DispatchQueue.main.async { [weak self] in self?.finishFrame(failed: true) }
                    return
                }
                encoder.setRenderPipelineState(state)
                encoder.setFragmentTexture(texture,index: 0)
                encoder.setFragmentTexture(patch,index: 1)
                var mode = mode, rect = rect, selected = selected, patchMode = patchMode
                encoder.setFragmentBytes(&mode,length: MemoryLayout<SIMD4<Float>>.size,index: 0)
                encoder.setFragmentBytes(&rect,length: MemoryLayout<SIMD4<Float>>.size,index: 1)
                encoder.setFragmentBytes(&selected,length: MemoryLayout<SIMD4<Float>>.size,index: 2)
                encoder.setFragmentBytes(&patchMode,length: MemoryLayout<Float>.size,index: 3)
                encoder.drawPrimitives(type: .triangleStrip,vertexStart: 0,vertexCount: 4)
                encoder.endEncoding()
                command.present(drawable)
                command.addCompletedHandler { [weak self] completed in
                    semaphore.signal()
                    let failed = completed.status == .error
                    DispatchQueue.main.async { [weak self] in self?.finishFrame(failed: failed) }
                }
                command.commit()
            }
        }
        return 0
    }
    private func finishFrame(failed: Bool) {
        pendingFrames -= 1
        if failed { self.failed = true; callback?(context); return }
        if waiting { waiting = false; callback?(context) }
    }
}

@_cdecl("rg_hdr_create")
public func createHdr(_ callback: (@convention(c) (UnsafeMutableRawPointer?) -> Void)?,
                      _ context: UnsafeMutableRawPointer?, _ libraryPath: UnsafePointer<CChar>?) -> UnsafeMutableRawPointer? {
    guard Thread.isMainThread, let libraryPath,
          let view = HdrView(libraryPath: String(cString: libraryPath)) else { return nil }
    view.callback = callback
    view.context = context
    return Unmanaged.passRetained(view).toOpaque()
}
@_cdecl("rg_hdr_present")
public func presentHdr(_ pointer: UnsafeMutableRawPointer?, _ pixels: UnsafeRawPointer?,
                       _ width: Int32, _ height: Int32, _ stride: Int32, _ white: Float,
                       _ alphaMode: Int32, _ smoothScaling: Int32) -> Int32 {
    guard Thread.isMainThread, let pointer, let pixels, width > 0, height > 0,
          Int64(stride) >= Int64(width) * 8, white.isFinite, white > 0 else { return -1 }
    return Unmanaged<HdrView>.fromOpaque(pointer).takeUnretainedValue().upload(
        pixels,Int(width),Int(height),Int(stride),white,alphaMode,smoothScaling)
}
@_cdecl("rg_hdr_headroom")
public func hdrHeadroom(_ pointer: UnsafeMutableRawPointer?) -> Float {
    guard Thread.isMainThread, let pointer,
          let screen = Unmanaged<HdrView>.fromOpaque(pointer).takeUnretainedValue().window?.screen else { return 1 }
    return Float(screen.maximumPotentialExtendedDynamicRangeColorComponentValue)
}
@_cdecl("rg_hdr_destroy")
public func destroyHdr(_ pointer: UnsafeMutableRawPointer?) {
    guard Thread.isMainThread, let pointer else { return }
    let view = Unmanaged<HdrView>.fromOpaque(pointer).takeRetainedValue()
    view.callback = nil
    view.removeFromSuperview()
}



@_cdecl("rg_hdr_patch")
public func hdrPatch(_ pointer: UnsafeMutableRawPointer?, _ pixels: UnsafeRawPointer?, _ width: Int32,
                     _ height: Int32, _ stride: Int32, _ x: Float, _ y: Float, _ w: Float, _ h: Float,
                     _ alphaMode: Int32) -> Int32 {
    guard Thread.isMainThread, let pointer else { return -1 }
    let view = Unmanaged<HdrView>.fromOpaque(pointer).takeUnretainedValue()
    guard let pixels else { view.patch = nil; view.patchRect = .zero; return 0 }
    guard width > 0, height > 0, Int64(stride) >= Int64(width)*8 else { return -1 }
    let descriptor = MTLTextureDescriptor.texture2DDescriptor(
        pixelFormat: .rgba16Float,width: Int(width),height: Int(height),mipmapped: false)
    descriptor.usage = .shaderRead
    descriptor.storageMode = view.device.hasUnifiedMemory ? .shared : .managed
    guard let texture = view.device.makeTexture(descriptor: descriptor) else { return -3 }
    texture.replace(region: MTLRegionMake2D(0,0,Int(width),Int(height)),mipmapLevel: 0,
                    withBytes: pixels,bytesPerRow: Int(stride))
    view.patch = texture
    view.patchRect = SIMD4<Float>(x,y,w,h)
    view.patchAlpha = Float(alphaMode)
    return 0
}
@_cdecl("rg_hdr_selection")
public func hdrSelection(_ pointer: UnsafeMutableRawPointer?, _ x: Float, _ y: Float, _ w: Float, _ h: Float) {
    guard Thread.isMainThread, let pointer else { return }
    let view = Unmanaged<HdrView>.fromOpaque(pointer).takeUnretainedValue()
    view.selection = SIMD4<Float>(x,y,w,h)
    view.drawFrame()
}
@_cdecl("rg_hdr_overlay_create")
public func createHdrOverlay(_ pointer: UnsafeMutableRawPointer?, _ parentWindow: UnsafeMutableRawPointer?) -> UnsafeMutableRawPointer? {
    guard Thread.isMainThread, let pointer, let parentWindow else { return nil }
    let owner = Unmanaged<NSWindow>.fromOpaque(parentWindow).takeUnretainedValue()
    let view = Unmanaged<HdrView>.fromOpaque(pointer).takeUnretainedValue()
    let panel = NSPanel(contentRect: owner.frame, styleMask: [.borderless,.nonactivatingPanel],
                        backing: .buffered,defer: false)
    panel.isReleasedWhenClosed = false
    panel.isOpaque = false
    panel.backgroundColor = .clear
    panel.ignoresMouseEvents = true
    panel.hidesOnDeactivate = false
    panel.collectionBehavior = [.canJoinAllSpaces,.fullScreenAuxiliary,.ignoresCycle]
    panel.contentView = view
    panel.level = owner.level
    owner.addChildWindow(panel,ordered: .below)
    panel.orderOut(nil)
    return Unmanaged.passRetained(panel).toOpaque()
}
@_cdecl("rg_hdr_overlay_show")
public func showHdrOverlay(_ pointer: UnsafeMutableRawPointer?, _ visible: Int32) {
    guard Thread.isMainThread, let pointer else { return }
    let panel = Unmanaged<NSPanel>.fromOpaque(pointer).takeUnretainedValue()
    if visible != 0, let owner = panel.parent {
        panel.setFrame(owner.frame,display: false)
        panel.level = owner.level
        panel.order(.below,relativeTo: owner.windowNumber)
    } else { panel.orderOut(nil) }
}
@_cdecl("rg_hdr_overlay_destroy")
public func destroyHdrOverlay(_ pointer: UnsafeMutableRawPointer?) {
    guard Thread.isMainThread, let pointer else { return }
    let panel = Unmanaged<NSPanel>.fromOpaque(pointer).takeRetainedValue()
    panel.parent?.removeChildWindow(panel)
    panel.orderOut(nil)
    panel.contentView = nil
    panel.close()
}
