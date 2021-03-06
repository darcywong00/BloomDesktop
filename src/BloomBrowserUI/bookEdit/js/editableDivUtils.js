/// <reference path="../../lib/jquery.d.ts" />
var EditableDivUtils = (function () {
    function EditableDivUtils() {
    }
    EditableDivUtils.getElementSelectionIndex = function (element) {
        var page = parent.window.document.getElementById('page');
        if (!page)
            return -1;

        var selection = page.contentWindow.getSelection();
        var active = $(selection.anchorNode).closest('div').get(0);
        if (active != element)
            return -1;
        if (!active || selection.rangeCount == 0) {
            return -1;
        }
        var myRange = selection.getRangeAt(0).cloneRange();
        myRange.setStart(active, 0);
        return myRange.toString().length;
    };

    EditableDivUtils.selectAtOffset = function (node, offset) {
        var iframeWindow = parent.window.document.getElementById('page').contentWindow;

        var range = iframeWindow.document.createRange();
        range.setStart(node, offset);
        range.setEnd(node, offset);
        var selection1 = iframeWindow.getSelection();
        selection1.removeAllRanges();
        selection1.addRange(range);
    };

    /**
    * Make a selection in the specified node at the specified offset.
    * If divBrCount is >=0, we expect to make the selection offset characters into node itself
    * (typically the root div). After traversing offset characters, we will try to additionally
    * traverse divBrCount <br> elements.
    * @param node
    * @param offset
    */
    EditableDivUtils.makeSelectionIn = function (node, offset, divBrCount, atStart) {
        if (node.nodeType === 3) {
            // drilled down to a text node. Make the selection.
            EditableDivUtils.selectAtOffset(node, offset);
            return true;
        }

        var i = 0;
        var childNode;
        var len;

        for (; i < node.childNodes.length && offset >= 0; i++) {
            childNode = node.childNodes[i];
            len = childNode.textContent.length;
            if (divBrCount >= 0 && len == offset) {
                for (i++; i < node.childNodes.length && divBrCount > 0 && node.childNodes[i].textContent.length == 0; i++) {
                    if (node.childNodes[i].localName === 'br')
                        divBrCount--;
                }

                // We want the selection in node itself, before childNode[i].
                EditableDivUtils.selectAtOffset(node, i);
                return true;
            }

            // If it's at the end of a child (that is not the last child) we have a choice whether to put it at the
            // end of that node or the start of the following one. For some reason the IP is invisible if
            // placed at the end of the preceding one, so prefer the start of the following one, which is why
            // we generally call this routine with atStart true.
            // (But, of course, if it is the last node we must be able to put the IP at the very end.)
            // When trying to do a precise restore, we pass atStart carefully, as it may control
            // whether we end up before or after some <br>s
            if (offset < len || (offset == len && (i == node.childNodes.length - 1 || !atStart))) {
                if (EditableDivUtils.makeSelectionIn(childNode, offset, -1, atStart)) {
                    return true;
                }
            }
            offset -= len;
        }

        for (i--; i >= 0; i--) {
            childNode = node.childNodes[i];
            len = childNode.textContent.length;
            if (EditableDivUtils.makeSelectionIn(childNode, len, -1, atStart)) {
                return true;
            }
        }

        // can't select anywhere (maybe this has no text-node children? Hopefully the caller can find
        // an equivalent place in an adjacent node).
        return false;
    };
    return EditableDivUtils;
})();
//# sourceMappingURL=editableDivUtils.js.map
