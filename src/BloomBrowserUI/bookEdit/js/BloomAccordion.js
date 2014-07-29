﻿/// <reference path="../../lib/jquery.d.ts" />

var BloomAccordion = (function () {
    function BloomAccordion() {
        $("#accordion").accordion({
            heightStyle: "fill",
            collapsible: true
        });
        $("#accordion").accordion('option', 'active', false);
    }
    BloomAccordion.Resize = function () {
        $("#accordion").accordion("refresh");
        //var myHeight = $(document).find(".accordionRoot").innerHeight();
        //console.log("Refreshed accordion to: "+myHeight);
    };
    return BloomAccordion;
})();
//# sourceMappingURL=BloomAccordion.js.map
