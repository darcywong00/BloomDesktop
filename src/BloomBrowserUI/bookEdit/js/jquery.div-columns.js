/// <reference path="../../lib/jquery.d.ts" />

/**
* Use an 'Immediately Invoked Function Expression' to make this compatible with jQuery.noConflict().
* @param {jQuery} $
*/
(function ($) {
    $.extend({
        /**
        * Set the width of the column (div) to be a multiple of the min-width.
        * NOTE: left and right margin are added for divs that span more than one column so the columns
        * will line up correctly.
        * @param {String} cssClassName
        */
        divsToColumns: function (cssClassName) {
            var div = $('div.' + cssClassName + ':first');
            if (div.length === 0)
                return;

            var minWidth = parseInt(div.css('min-width'));
            var marginLeft = parseInt(div.css('margin-left'));
            var marginRight = parseInt(div.css('margin-right'));

            // limit the list to elements with text wider than min-width allows
            var elements = $('div.' + cssClassName).filter(function () {
                return this.offsetWidth > minWidth;
            });

            elements.css('width', function () {
                var w = this.offsetWidth;
                var i = Math.ceil(w / minWidth);
                w = minWidth * i + (i - 1) * (marginLeft + marginRight);
                return w + 'px';
            });
        },
        /**
        * Sets the number of columns based on the width of the longest word.
        * NOTE: This method was added because divsToColumns is too slow for lists of more that a few hundred items.
        * @param {String} cssClassName
        * @param {String} longestWord
        */
        divsToColumnsBasedOnLongestWord: function (cssClassName, longestWord) {
            var div = $('div.' + cssClassName + ':first');
            if (div.length === 0)
                return;

            var parent = div.parent();
            var parentWidth = parent.width() - 20;

            var elements = $('div.' + cssClassName);
            if (elements.length === 0)
                return;

            var maxWidth = textWidth(div, longestWord);

            var colCount = Math.floor(parentWidth / maxWidth);
            parent.css('column-count', colCount);
        }
    });

    /**
    * Calculates the width of an element containing the specified text
    * @param {HTMLDivElement} div
    * @param {String} text
    * @returns {number}
    */
    function textWidth(div, text) {
        var _t = jQuery(div);
        var html_calcS = '<span>' + text + '</span>';
        jQuery('body').append(html_calcS);

        var _lastSpan = jQuery('span').last();
        _lastSpan.css({
            'font-size': _t.css('font-size'),
            'font-family': _t.css('font-family')
        });

        var width = _lastSpan.width() + 5;

        _lastSpan.remove();
        return width;
    }
}(jQuery));
//# sourceMappingURL=jquery.div-columns.js.map
