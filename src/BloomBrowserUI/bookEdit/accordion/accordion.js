
/**
 * Fires an event for C# to handle
 * @param {type} eventName
 * @param {type} eventData
 */
function fireCSharpAccordionEvent(eventName, eventData) {

    var event = new MessageEvent(eventName, {'view' : window, 'bubbles' : true, 'cancelable' : true, 'data' : eventData});
    document.dispatchEvent(event);
}

function checkbox_click(chkbox) {

    var panel = $(chkbox).data('panel');

    if (chkbox.innerHTML === '') {
        chkbox.innerHTML = '&#10004;';
        fireCSharpAccordionEvent('saveAccordionSettingsEvent', chkbox.id + "\t1");
        if (panel) fireCSharpAccordionEvent('loadAccordionPanelEvent', panel);
    }
    else {
        chkbox.innerHTML = '';
        fireCSharpAccordionEvent('saveAccordionSettingsEvent', chkbox.id + "\t0");
        $('*:data(panelId)').filter(function() { return $(this).data('panelId') === panel; }).remove();
    }
}

/**
 * Called by C# to restore user settings
 * @param {type} settings
 */
function restoreAccordionSettings(settings) {

    var opts = JSON.parse(settings);
    var check = '&#10004;';

    if (opts['showPE'])
        requestPanel('showPE', 'PageElements');

    if (opts['showDRT'])
        requestPanel('showDRT', 'DecodableRT');

    if (opts['showLRT'])
        requestPanel('showLRT', 'LeveledRT');

    // set the current panel
    if (opts['current']) {

        // find the index of the panel with the 'current' id
        $('#accordion > h3').each(function() {
            if (($(this).data('panelId')) && ($(this).data('panelId') === opts['current'])) {

                // the index is the last segment of the element id
                var idx = this.id.substr(this.id.lastIndexOf('-') + 1);

                // turn off animation
                var ani = $('#accordion').accordion('option', 'animate');
                $('#accordion').accordion('option', 'animate', false);

                // the index must be passed as an int, a string will not work
                $('#accordion').accordion('option', 'active', parseInt(idx));

                // trun animation back on
                $('#accordion').accordion('option', 'animate', ani);

                // break from the each() loop
                return false;
            }
        });
    }
}

function requestPanel(checkBoxId, panelId) {

    // this is the character code for a check mark
    var check = '&#10004;';

    var chkBox = document.getElementById(checkBoxId);
    if (chkBox) {
        chkBox.innerHTML = check;
        fireCSharpAccordionEvent('loadAccordionPanelEvent', panelId);
    }
}

function loadAccordionPanel(newContent, panelId) {

    var elements = $.parseHTML(newContent, document, true);

    $.each(elements, function() {
        if (this.nodeName !== 'SCRIPT') {

            $(this).data('panelId', panelId);
            $(this).insertBefore('#accordion-settings-header');
        }
    });

    $('#accordion').accordion('refresh');

    $('#accordion').onOnce('accordionactivate.accordion', function(event, ui) {
        // remember current panel
        //fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t" + $('#accordion').accordion('option', 'active'));
        if (ui.newHeader.data('panelId'))
            fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t" + ui.newHeader.data('panelId').toString());
        else
            fireCSharpAccordionEvent('saveAccordionSettingsEvent', "current\t");
    });

}
