#import <AppKit/AppKit.h>
#include <stdint.h>

typedef void (*rg_tray_callback)(void *, int, int);

@interface RGTray : NSObject <NSMenuDelegate>
@property(strong) NSStatusItem *item;
@property(strong) NSMenu *menu;
@property(strong) NSMutableDictionary<NSNumber *, NSMenu *> *menus;
@property(assign) rg_tray_callback callback;
@property(assign) void *context;
- (void)pressed:(id)sender;
- (void)selected:(NSMenuItem *)sender;
@end

@implementation RGTray
- (void)pressed:(id)sender
{
    (void)sender;
    if (self.callback == NULL) return;
    NSEvent *event = NSApp.currentEvent;
    if (event.buttonNumber == 1) {
        self.callback(self.context, 4, 0);
        if (self.menu != nil) {
            NSRect bounds = self.item.button.bounds;
            [self.menu popUpMenuPositioningItem:nil atLocation:NSMakePoint(0, NSMinY(bounds)) inView:self.item.button];
        }
    } else if (event.buttonNumber == 2) {
        self.callback(self.context, 3, 0);
    } else if (event.buttonNumber == 0) {
        self.callback(self.context, event.clickCount > 1 ? 2 : 1, 0);
    }
}
- (void)selected:(NSMenuItem *)sender
{
    if (self.callback == NULL) return;
    self.callback(self.context, 5, (int)sender.tag);
}
- (void)menuWillOpen:(NSMenu *)menu
{
    if (self.callback == NULL) return;
    for (NSNumber *key in self.menus) {
        if (self.menus[key] == menu) {
            self.callback(self.context, 6, key.intValue);
            break;
        }
    }
}
- (void)menuDidClose:(NSMenu *)menu
{
    if (self.callback == NULL) return;
    for (NSNumber *key in self.menus) {
        if (self.menus[key] == menu) {
            self.callback(self.context, 7, key.intValue);
            break;
        }
    }
}
@end

static NSString *rg_string(const char *value)
{
    return value == NULL ? @"" : [NSString stringWithUTF8String:value];
}

__attribute__((visibility("default"))) void *rg_tray_create(rg_tray_callback callback, void *context)
{
    if (![NSThread isMainThread] || callback == NULL) return NULL;
    RGTray *tray = [RGTray new];
    tray.callback = callback;
    tray.context = context;
    tray.item = [NSStatusBar.systemStatusBar statusItemWithLength:NSSquareStatusItemLength];
    if (tray.item == nil) return NULL;
    tray.menus = [NSMutableDictionary new];
    tray.item.button.target = tray;
    tray.item.button.action = @selector(pressed:);
    [tray.item.button sendActionOn:NSEventMaskLeftMouseUp | NSEventMaskRightMouseUp | NSEventMaskOtherMouseUp];
    return (__bridge_retained void *)tray;
}

__attribute__((visibility("default"))) void rg_tray_dispose(void *handle)
{
    if (handle == NULL) return;
    RGTray *tray = (__bridge_transfer RGTray *)handle;
    tray.callback = NULL;
    tray.context = NULL;
    tray.item.button.target = nil;
    tray.item.button.action = NULL;
    [NSStatusBar.systemStatusBar removeStatusItem:tray.item];
    tray.item = nil;
    tray.menu = nil;
    [tray.menus removeAllObjects];
}

__attribute__((visibility("default"))) void rg_tray_icon(void *handle, const uint8_t *bytes, int length)
{
    RGTray *tray = (__bridge RGTray *)handle;
    if (bytes == NULL || length <= 0) {
        tray.item.button.image = nil;
        return;
    }
    NSImage *image = [[NSImage alloc] initWithData:[NSData dataWithBytes:bytes length:(NSUInteger)length]];
    if (image.size.height > 0) {
        CGFloat height = MAX(1, NSStatusBar.systemStatusBar.thickness - 4);
        image.size = NSMakeSize(image.size.width * height / image.size.height, height);
    }
    image.template = NO;
    tray.item.button.image = image;
}

__attribute__((visibility("default"))) void rg_tray_visible(void *handle, int visible)
{
    ((__bridge RGTray *)handle).item.visible = visible != 0;
}

__attribute__((visibility("default"))) void rg_tray_text(void *handle, const char *text)
{
    ((__bridge RGTray *)handle).item.button.toolTip = rg_string(text);
}

__attribute__((visibility("default"))) void rg_tray_menu_reset(void *handle)
{
    RGTray *tray = (__bridge RGTray *)handle;
    tray.menu = [NSMenu new];
    tray.menu.autoenablesItems = NO;
    tray.menu.delegate = tray;
    [tray.menus removeAllObjects];
    tray.menus[@0] = tray.menu;
}

__attribute__((visibility("default"))) void rg_tray_menu_item(void *handle, int identifier, int parent,
    int enabled, int toggle, int checked, int separator, const char *label, const char *key, uint32_t modifiers)
{
    RGTray *tray = (__bridge RGTray *)handle;
    NSMenu *menu = tray.menus[@(parent)];
    if (menu == nil) return;
    NSMenuItem *item;
    if (separator) item = NSMenuItem.separatorItem;
    else {
        item = [[NSMenuItem alloc] initWithTitle:rg_string(label) action:@selector(selected:) keyEquivalent:rg_string(key)];
        item.tag = identifier;
        item.target = tray;
        item.enabled = enabled != 0;
        if (toggle != 0) item.state = checked ? NSControlStateValueOn : NSControlStateValueOff;
        NSEventModifierFlags flags = 0;
        if (modifiers & 1) flags |= NSEventModifierFlagShift;
        if (modifiers & 2) flags |= NSEventModifierFlagControl;
        if (modifiers & 4) flags |= NSEventModifierFlagOption;
        if (modifiers & 8) flags |= NSEventModifierFlagCommand;
        item.keyEquivalentModifierMask = flags;
    }
    [menu addItem:item];
    NSMenu *submenu = [NSMenu new];
    submenu.autoenablesItems = NO;
    submenu.delegate = tray;
    tray.menus[@(identifier)] = submenu;
}

__attribute__((visibility("default"))) void rg_tray_menu_commit(void *handle)
{
    RGTray *tray = (__bridge RGTray *)handle;
    for (NSMenu *menu in tray.menus.allValues) {
        for (NSMenuItem *item in menu.itemArray) {
            NSMenu *submenu = tray.menus[@(item.tag)];
            if (!item.separatorItem && submenu.numberOfItems > 0) item.submenu = submenu;
        }
    }
}
