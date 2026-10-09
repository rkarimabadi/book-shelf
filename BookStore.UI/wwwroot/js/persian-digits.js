// نون گرد — shows every Latin digit (0-9) in the rendered UI as a Persian digit (۰-۹).
// Display only: it rewrites the text of DOM text nodes, never attributes, input values or URLs,
// so data sent to the API and copied from inputs is unchanged. Text inside an element marked
// dir="ltr" or data-keep-latin (e-mail addresses, codes) is left as typed.
(function () {
    'use strict';

    var SKIP = 'script,style,textarea,input,select,option,code,pre,[dir="ltr"],[data-keep-latin]';
    var LATIN = /[0-9]/;

    function convert(text) {
        return text.replace(/[0-9]/g, function (d) { return String.fromCharCode(0x06F0 + d.charCodeAt(0) - 48); });
    }

    function fixTextNode(node) {
        var value = node.nodeValue;
        if (!value || !LATIN.test(value)) { return; }
        var parent = node.parentElement;
        if (!parent || parent.closest(SKIP)) { return; }
        node.nodeValue = convert(value);
    }

    function fixTree(root) {
        if (root.nodeType === 3) { fixTextNode(root); return; }
        if (root.nodeType !== 1) { return; }
        var walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
        var node;
        while ((node = walker.nextNode())) { fixTextNode(node); }
    }

    function start() {
        fixTree(document.body);
        new MutationObserver(function (records) {
            for (var i = 0; i < records.length; i++) {
                var r = records[i];
                if (r.type === 'characterData') { fixTextNode(r.target); continue; }
                for (var j = 0; j < r.addedNodes.length; j++) { fixTree(r.addedNodes[j]); }
            }
        }).observe(document.body, { childList: true, subtree: true, characterData: true });
    }

    if (document.body) { start(); } else { document.addEventListener('DOMContentLoaded', start); }
})();
