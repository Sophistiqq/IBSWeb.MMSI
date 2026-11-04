const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const view = fs.readFileSync(path.join(__dirname, '../../IBSWeb/Areas/MSAP/Views/AuditTrail/Index.cshtml'), 'utf8');
assert.match(view, /id="traceLoading"[^>]* hidden[ >]/, 'The loader must start hidden without depending on a CSS utility class.');
const script = [...view.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(match => match[1]).find(value => value.includes('function loadTrace'));
const elements = new Map();
function element(selector) {
    if (!elements.has(selector)) {
        const state = { hidden: selector === '#traceLoading', visible: true, content: '', value: '' };
        const api = {
            ready() { return api; },
            prop(name, value) { if (value === undefined) return state[name]; state[name] = value; return api; },
            attr(name, value) { state[name] = value; return api; },
            is() { return state.visible; },
            show() { state.visible = true; return api; },
            hide() { state.visible = false; return api; },
            html(value) { state.content = value; return api; },
            text(value) { state.content = value; return api; },
            val(value) { if (value === undefined) return state.value; state.value = value; return api; },
            trigger() { return api; },
            state
        };
        elements.set(selector, api);
    }
    return elements.get(selector);
}
const requests = [];
function request(options) {
    assert.equal(options.timeout, 30000, 'A stalled request must time out.');
    const callbacks = {};
    const xhr = {
        done(fn) { callbacks.done = fn; return xhr; },
        fail(fn) { callbacks.fail = fn; return xhr; },
        always(fn) { callbacks.always = fn; return xhr; },
        abort() { xhr.aborted = true; xhr.reject('abort'); },
        resolve(html) { callbacks.done(html); callbacks.always(); },
        reject(status) { callbacks.fail({ status: 500, responseText: '<script>private error</script>' }, status); callbacks.always(); }
    };
    requests.push(xhr);
    return xhr;
}
const jquery = selector => element(selector);
jquery.ajax = request;
const sandbox = { $: jquery, document: {}, console, refreshModernSelect() {} };
vm.createContext(sandbox);
vm.runInContext(script.replace(/@Url.Action\([^\n]*?\)/g, '/timeline'), sandbox);
sandbox.loadTrace('1', 'First COS');
assert.equal(element('#traceLoading').state.hidden, false);
requests[0].resolve('<p>Loaded history</p>');
assert.equal(element('#traceTimeline').state.content, '<p>Loaded history</p>');
assert.equal(element('#traceLoading').state.hidden, true);
assert.equal(element('#traceTimeline').state['aria-busy'], 'false');
sandbox.loadTrace('2', 'Slow COS');
sandbox.loadTrace('3', 'Current COS');
assert.equal(requests[1].aborted, true);
requests[1].resolve('<p>Stale history</p>');
assert.equal(element('#traceTimeline').state.content, '', 'An old response cannot replace the current selection.');
assert.equal(element('#traceLoading').state.hidden, false, 'An old completion cannot hide the current loader.');
requests[2].reject('timeout');
assert.match(element('#traceTimeline').state.content, /timed out/i);
assert(!element('#traceTimeline').state.content.includes('private error'), 'Server error details must not be displayed.');
assert.equal(element('#traceLoading').state.hidden, true);
sandbox.loadTrace('4', 'Failed COS');
requests[3].reject('error');
assert.match(element('#traceTimeline').state.content, /unable to load history/i);
assert.equal(element('#traceLoading').state.hidden, true);
sandbox.loadTrace('5', 'Clear while loading');
sandbox.clearTrace();
assert.equal(requests[4].aborted, true);
requests[4].resolve('<p>Late history</p>');
assert.equal(element('#traceLoading').state.hidden, true);
assert.equal(element('#traceResult').state.visible, false);
assert.equal(element('#tracePlaceholder').state.visible, true);
assert.equal(element('#traceTimeline').state.content, '');
console.log('PASS: workflow trace success, timeout, overlapping requests and clear while loading finish without stale results.');
