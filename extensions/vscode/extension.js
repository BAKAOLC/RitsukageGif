'use strict';
const vscode = require('vscode');
const path = require('path');
const fs = require('fs');
const { spawn } = require('child_process');
const messages = require(vscode.env.language.toLowerCase().startsWith('zh') ? './package.nls.zh-cn.json' : './package.nls.json');
const text = key => messages[key] || key;
const source = file => file ? { name: path.basename(file), path: file } : undefined;
const executablePath = value => process.platform === 'darwin' && value.toLowerCase().endsWith('.app')
  ? path.join(value, 'Contents', 'MacOS', 'RitsukageGif') : value;

class Bridge {
  constructor(executable, diagnostics) {
    this.next = 0; this.pending = new Map(); this.buffer = Buffer.alloc(0); this.closed = false;
    this.process = spawn(executable, ['--language-server'], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    this.process.stderr.on('data', () => {});
    this.process.stdin.on('error', error => this.fail(error));
    this.process.stdout.on('error', error => this.fail(error));
    this.process.on('error', error => this.fail(error));
    this.process.on('exit', () => this.fail(new Error(text('closed'))));
    this.process.stdout.on('data', chunk => {
      this.buffer = Buffer.concat([this.buffer, chunk]);
      while (true) {
        const end = this.buffer.indexOf('\r\n\r\n'); if (end < 0) break;
        const match = /Content-Length:\s*(\d+)/i.exec(this.buffer.subarray(0, end).toString('ascii'));
        if (!match || Number(match[1]) > 4 * 1024 * 1024) { this.dispose(); break; }
        const length = Number(match[1]); if (this.buffer.length < end + 4 + length) break;
        let message;
        try { message = JSON.parse(this.buffer.subarray(end + 4, end + 4 + length).toString('utf8')); }
        catch (error) { this.fail(error); this.dispose(); break; }
        this.buffer = this.buffer.subarray(end + 4 + length);
        if (message.id !== undefined) {
          const pending = this.pending.get(message.id); this.pending.delete(message.id);
          if (pending) { clearTimeout(pending.timer); message.error ? pending.reject(new Error(message.error.message)) : pending.resolve(message.result); }
        } else if (message.method === 'textDocument/publishDiagnostics') diagnostics?.(message.params);
      }
    });
  }
  fail(error) { this.closed = true; for (const pending of this.pending.values()) { clearTimeout(pending.timer); pending.reject(error); } this.pending.clear(); }
  send(message) {
    if (this.closed) throw new Error(text('closed'));
    const body = Buffer.from(JSON.stringify({ jsonrpc: '2.0', ...message }));
    this.process.stdin.write(`Content-Length: ${body.length}\r\n\r\n`); this.process.stdin.write(body);
  }
  notify(method, params) { this.send({ method, params }); }
  request(method, params) {
    const id = ++this.next;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error(text('closed'))); }, 60000);
      this.pending.set(id, { resolve, reject, timer });
      try { this.send({ id, method, params }); } catch (error) { clearTimeout(timer); this.pending.delete(id); reject(error); }
    });
  }
  async command(args) {
    const result = await this.request('rgscript/command', { arguments: args });
    if (!result?.Success) throw new Error(result?.Message || text('closed')); return result;
  }
  dispose() { if (!this.closed) { try { this.notify('exit', {}); } catch {} } this.process.kill(); this.fail(new Error(text('closed'))); }
}

class DebugAdapter {
  constructor(bridge) {
    this.bridge = bridge; this.sequence = 0; this.events = new vscode.EventEmitter(); this.onDidSendMessage = this.events.event;
    this.breakpoints = new Map(); this.disposed = false; this.started = false; this.lastOutput = []; this.lastPaused = false;
  }
  send(message) { this.events.fire({ seq: ++this.sequence, ...message }); }
  event(event, body) { this.send({ type: 'event', event, body }); }
  async handleMessage(request) {
    const respond = (body = {}) => this.send({ type: 'response', request_seq: request.seq, command: request.command, success: true, body });
    try {
      const args = request.arguments || {};
      switch (request.command) {
        case 'initialize':
          respond({ supportsConfigurationDoneRequest: true, supportsTerminateRequest: true, supportsEvaluateForHovers: true, supportsExceptionInfoRequest: true }); this.event('initialized'); break;
        case 'launch': {
          if (!vscode.workspace.isTrusted) throw new Error(text('untrusted'));
          this.file = path.resolve(args.program); this.stopOnEntry = args.stopOnEntry !== false;
          await vscode.workspace.saveAll(false);
          await this.bridge.command(['debug', 'run', this.file, args.preset || '', 'true']); this.started = true;
          for (const [file, lines] of this.breakpoints) await this.bridge.command(['debug', 'breakpoints', file, lines.join(',')]);
          respond(); break;
        }
        case 'setBreakpoints': {
          const file = path.resolve(args.source.path); const lines = (args.breakpoints || []).map(b => b.line);
          const executable = await this.bridge.request('rgscript/executableLines', { text: await fs.promises.readFile(file, 'utf8') });
          this.breakpoints.set(file, lines.filter(line => executable.includes(line)));
          if (this.started) await this.bridge.command(['debug', 'breakpoints', file, this.breakpoints.get(file).join(',')]);
          respond({ breakpoints: lines.map(line => ({ verified: executable.includes(line), line })) }); break;
        }
        case 'configurationDone':
          if (!this.stopOnEntry) await this.bridge.command(['debug', 'continue']);
          respond(); this.poll(); break;
        case 'threads': respond({ threads: [{ id: 1, name: 'rgscript' }] }); break;
        case 'stackTrace': {
          const state = this.snapshot;
          const frames = state?.StackFrames?.length ? state.StackFrames : state ? [{ Name: state.Frames?.at(-1) || state.State || 'script', Source: state.Source || this.file, Line: state.Line, Column: state.Column }] : [];
          const start = Math.max(0, args.startFrame || 0);
          const end = args.levels > 0 ? start + args.levels : frames.length;
          respond({ stackFrames: frames.slice(start, end).map((frame, index) => ({ id: start + index + 1, name: frame.Name || 'script', source: source(frame.Source || this.file), line: Math.max(1, frame.Line || 1), column: Math.max(1, frame.Column || 1) })), totalFrames: frames.length }); break;
        }
        case 'exceptionInfo': {
          const diagnostic = this.snapshot?.ErrorDiagnostic;
          respond({ exceptionId: 'ScriptError', description: diagnostic?.Message || this.snapshot?.Error || '', breakMode: 'unhandled', details: { message: diagnostic?.Message || this.snapshot?.Error || '' } }); break;
        }
        case 'scopes': respond({ scopes: args.frameId && args.frameId !== 1 ? [] : [{ name: text('variables'), variablesReference: 1, expensive: false }] }); break;
        case 'variables': respond({ variables: Object.entries(this.snapshot?.Variables || {}).map(([name, value]) => ({ name, value, variablesReference: 0 })) }); break;
        case 'evaluate': {
          if (args.frameId && args.frameId !== 1) throw new Error(text('unknownVariable'));
          const value = this.snapshot?.Variables?.[args.expression]; if (value === undefined) throw new Error(text('unknownVariable'));
          respond({ result: value, variablesReference: 0 }); break;
        }
        case 'continue': case 'next': case 'stepIn':
          if (this.failed) { respond({ allThreadsContinued: true }); this.event('terminated'); this.dispose(); break; }
          await this.bridge.command(['debug', request.command === 'continue' ? 'continue' : 'step']);
          this.lastPaused = false; respond({ allThreadsContinued: true }); this.event('continued', { threadId: 1, allThreadsContinued: true }); break;
        case 'pause': await this.bridge.command(['debug', 'pause']); respond(); break;
        case 'disconnect': case 'terminate':
          if (this.started && !this.failed) await this.bridge.command(['debug', 'stop']); respond(); this.event('terminated'); this.dispose(); break;
        default: respond(); break;
      }
    } catch (error) { this.send({ type: 'response', request_seq: request.seq, command: request.command, success: false, message: error.message }); }
  }
  async poll() {
    if (this.disposed) return;
    try {
      const status = await this.bridge.command(['status']); this.snapshot = status.Debug;
      const output = this.snapshot?.Output || []; let overlap = Math.min(this.lastOutput.length, output.length);
      while (overlap && JSON.stringify(this.lastOutput.slice(-overlap)) !== JSON.stringify(output.slice(0, overlap))) overlap--;
      for (const line of output.slice(overlap)) this.event('output', { category: 'console', output: line + '\n' }); this.lastOutput = output;
      if (!this.snapshot || this.snapshot.Finished) {
        if (this.snapshot?.Error) {
          const diagnostic = this.snapshot.ErrorDiagnostic;
          this.event('output', { category: 'stderr', output: (diagnostic?.Message || this.snapshot.Error) + '\n', source: source(diagnostic?.Source || this.snapshot.Source || this.file), line: Math.max(1, diagnostic?.Line || this.snapshot.Line || 1), column: Math.max(1, diagnostic?.Column || this.snapshot.Column || 1) });
          for (const related of diagnostic?.Related || []) this.event('output', { category: 'stderr', output: related.Message + '\n', source: source(related.Source), line: Math.max(1, related.Line || 1), column: Math.max(1, related.Column || 1) });
          this.failed = true;
          this.event('stopped', { reason: 'exception', description: diagnostic?.Message || this.snapshot.Error, threadId: 1, allThreadsStopped: true });
          return;
        }
        this.event('terminated'); this.dispose(); return;
      }
      if (this.snapshot.Paused && !this.lastPaused) this.event('stopped', { reason: 'breakpoint', threadId: 1, allThreadsStopped: true });
      this.lastPaused = this.snapshot.Paused;
    } catch (error) { this.event('output', { category: 'stderr', output: error.message + '\n' }); this.event('terminated'); this.dispose(); return; }
    this.timer = setTimeout(() => this.poll(), 100);
  }
  dispose() { if (this.disposed) return; this.disposed = true; clearTimeout(this.timer); this.bridge.dispose(); this.events.dispose(); }
}

function activate(context) {
  const diagnosticCollection = vscode.languages.createDiagnosticCollection('rgscript');
  context.subscriptions.push(diagnosticCollection);
  let languageBridge; const timers = new Map();
  const range = r => new vscode.Range(r.start.line, r.start.character, r.end.line, r.end.character);
  const publish = params => diagnosticCollection.set(vscode.Uri.parse(params.uri), params.diagnostics.map(d => {
    const diagnostic = new vscode.Diagnostic(range(d.range), d.message, [vscode.DiagnosticSeverity.Error, vscode.DiagnosticSeverity.Warning, vscode.DiagnosticSeverity.Information, vscode.DiagnosticSeverity.Hint][(d.severity || 1) - 1] ?? vscode.DiagnosticSeverity.Error);
    diagnostic.source = d.source || 'rgscript';
    diagnostic.code = d.code;
    diagnostic.relatedInformation = (d.relatedInformation || []).map(related => new vscode.DiagnosticRelatedInformation(new vscode.Location(vscode.Uri.parse(related.location.uri), range(related.location.range)), related.message));
    return diagnostic;
  }));
  const configured = () => executablePath(vscode.workspace.getConfiguration('rgscript').get('executable', ''));
  const service = async () => {
    if (!languageBridge || languageBridge.closed) {
      const executable = configured() || context.asAbsolutePath('server/RitsukageGif.LanguageServer' + (process.platform === 'win32' ? '.exe' : ''));
      languageBridge = new Bridge(executable, publish);
      await languageBridge.request('initialize', { processId: process.pid, capabilities: {} });
      for (const doc of vscode.workspace.textDocuments) if (doc.languageId === 'rgscript') sync(doc);
    }
    return languageBridge;
  };
  function sync(document) {
    if (document.languageId !== 'rgscript' || !languageBridge || languageBridge.closed) return;
    languageBridge.notify('textDocument/didOpen', { textDocument: { uri: document.uri.toString(), languageId: 'rgscript', version: document.version, text: document.getText() } });
  }
  const request = async (method, doc, position) => {
    const bridge = await service(); sync(doc);
    return bridge.request(method, { textDocument: { uri: doc.uri.toString() }, position: position ? { line: position.line, character: position.character } : undefined });
  };
  context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(doc => { if (doc.languageId === 'rgscript') service().then(() => sync(doc)).catch(e => vscode.window.showErrorMessage(e.message)); }));
  context.subscriptions.push(vscode.workspace.onDidChangeTextDocument(e => {
    if (e.document.languageId !== 'rgscript') return;
    clearTimeout(timers.get(e.document.uri.toString())); timers.set(e.document.uri.toString(), setTimeout(() => sync(e.document), 250));
  }));
  context.subscriptions.push(vscode.workspace.onDidCloseTextDocument(doc => {
    clearTimeout(timers.get(doc.uri.toString())); timers.delete(doc.uri.toString()); diagnosticCollection.delete(doc.uri);
    if (languageBridge && !languageBridge.closed) languageBridge.notify('textDocument/didClose', { textDocument: { uri: doc.uri.toString() } });
  }));
  context.subscriptions.push(vscode.languages.registerCompletionItemProvider('rgscript', {
    async provideCompletionItems(doc, pos) {
      const results = await request('textDocument/completion', doc, pos);
      return (results || []).map(item => { const result = new vscode.CompletionItem(item.label, ({ 2: vscode.CompletionItemKind.Method, 3: vscode.CompletionItemKind.Function, 6: vscode.CompletionItemKind.Variable, 10: vscode.CompletionItemKind.Property, 20: vscode.CompletionItemKind.EnumMember, 7: vscode.CompletionItemKind.Class, 9: vscode.CompletionItemKind.Module, 13: vscode.CompletionItemKind.Enum })[item.kind] ?? vscode.CompletionItemKind.Keyword); result.insertText = item.insertText; result.detail = item.detail; result.range = doc.getWordRangeAtPosition(pos, /[\w]+/); return result; });
    }
  }, '.'));
  context.subscriptions.push(vscode.languages.registerSignatureHelpProvider('rgscript', {
    async provideSignatureHelp(doc, pos) {
      const result = await request('textDocument/signatureHelp', doc, pos);
      if (!result) return undefined;
      const help = new vscode.SignatureHelp();
      help.activeSignature = result.activeSignature; help.activeParameter = result.activeParameter;
      help.signatures = result.signatures.map(item => {
        const signature = new vscode.SignatureInformation(item.label, item.documentation);
        signature.parameters = item.parameters.map(parameter => new vscode.ParameterInformation(parameter.label));
        return signature;
      });
      return help;
    }
  }, '(', ','));
  context.subscriptions.push(vscode.languages.registerHoverProvider('rgscript', { async provideHover(doc, pos) { const result = await request('textDocument/hover', doc, pos); return result ? new vscode.Hover(new vscode.MarkdownString().appendCodeblock(result.contents.value, 'rgscript'), range(result.range)) : undefined; } }));
  context.subscriptions.push(vscode.languages.registerDefinitionProvider('rgscript', { async provideDefinition(doc, pos) { const result = await request('textDocument/definition', doc, pos); return result ? new vscode.Location(vscode.Uri.parse(result.uri), range(result.range)) : undefined; } }));
  context.subscriptions.push(vscode.languages.registerDocumentSymbolProvider('rgscript', { async provideDocumentSymbols(doc) { return (await request('textDocument/documentSymbol', doc)).map(s => new vscode.DocumentSymbol(s.name, '', s.kind === 12 ? vscode.SymbolKind.Function : vscode.SymbolKind.Variable, range(s.range), range(s.selectionRange))); } }));
  async function runtime() {
    let executable = configured();
    if (!executable || !fs.existsSync(executable)) {
      const selected = await vscode.window.showOpenDialog({ title: text('chooseExecutable'), canSelectMany: false,
        canSelectFolders: process.platform === 'darwin', canSelectFiles: true,
        ...(process.platform === 'win32' ? { filters: { Executable: ['exe'] } } : {}) });
      if (!selected?.length) throw new Error(text('noExecutable'));
      executable = executablePath(selected[0].fsPath); await vscode.workspace.getConfiguration('rgscript').update('executable', executable, vscode.ConfigurationTarget.Global);
    }
    return new Bridge(executable);
  }
  context.subscriptions.push(vscode.debug.registerDebugAdapterDescriptorFactory('rgscript', {
    async createDebugAdapterDescriptor() { return new vscode.DebugAdapterInlineImplementation(new DebugAdapter(await runtime())); }
  }));
  for (const debug of [false, true]) context.subscriptions.push(vscode.commands.registerCommand(debug ? 'rgscript.debug' : 'rgscript.run', async () => {
    if (!vscode.workspace.isTrusted) return vscode.window.showErrorMessage(text('untrusted'));
    const doc = vscode.window.activeTextEditor?.document; if (!doc || doc.languageId !== 'rgscript') return vscode.window.showErrorMessage(text('noDocument'));
    if (!await doc.save()) return;
    return vscode.debug.startDebugging(vscode.workspace.getWorkspaceFolder(doc.uri), { type: 'rgscript', request: 'launch', name: 'rgscript', program: doc.uri.fsPath, stopOnEntry: debug });
  }));
  context.subscriptions.push(vscode.workspace.onDidChangeConfiguration(e => { if (e.affectsConfiguration('rgscript.executable')) { languageBridge?.dispose(); languageBridge = undefined; } }));
  context.subscriptions.push({ dispose() { for (const timer of timers.values()) clearTimeout(timer); languageBridge?.dispose(); } });
  service().catch(e => vscode.window.showErrorMessage(e.message));
}
exports.activate = activate;
