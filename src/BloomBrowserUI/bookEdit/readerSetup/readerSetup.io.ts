/// <reference path="../js/getIframeChannel.ts" />
/// <reference path="../js/readerSettings.ts" />
/// <reference path="../js/libsynphony/underscore-1.5.2.d.ts" />
/// <reference path="readerSetup.ui.ts" />

var previousMoreWords: string;

window.addEventListener('message', process_IO_Message, false);

function accordionWindow(): Window {
  if (window.parent)
    return (<HTMLIFrameElement>window.parent.document.getElementById('accordion')).contentWindow;
}

function process_IO_Message(event: MessageEvent): void {

  var params = event.data.split("\n");

  switch(params[0]) {
    case 'OK':
      saveClicked();
      return;

    case 'Data':
      loadReaderSetupData(params[1]);
      accordionWindow().postMessage('SetupType', '*');
      return;

    default:
  }
}

/**
 * Initializes the dialog with the current settings.
 * @param {String} jsonData The contents of the settings file
 */
function loadReaderSetupData(jsonData: string): void {

  if ((!jsonData) || (jsonData === '""')) return;

  // validate data
  var data = JSON.parse(jsonData);
  if (!data.letters) data.letters = '';
  if (!data.moreWords) data.moreWords = '';
  if (!data.stages) data.stages = [];
  if (!data.levels) data.levels = [];
  if (data.stages.length === 0) data.stages.push(new ReaderStage('1'));
  if (data.levels.length === 0) data.levels.push(new ReaderLevel('1'));

  // language tab
  (<HTMLInputElement>document.getElementById('dls_letters')).value = data.letters;
  previousMoreWords = data.moreWords.replace(/ /g, '\n');
  (<HTMLInputElement>document.getElementById('dls_more_words')).value = previousMoreWords;

  // stages tab
  displayLetters();
  var stages = data.stages;
  var tbody = $('#stages-table').find('tbody');
  tbody.html('');

  for (var i = 0; i < stages.length; i++) {
    if (!stages[i].letters) stages[i].letters = '';
    if (!stages[i].sightWords) stages[i].sightWords = '';
    tbody.append('<tr class="linked"><td>' + (i + 1) + '</td><td class="book-font">' + stages[i].letters + '</td><td class="book-font">' + stages[i].sightWords + '</td></tr>');
  }

  // click event for stage rows
  tbody.find('tr').onOnce('click', function() {
    selectStage(this);
    displayLetters();
    selectLetters(this);
  });

  // levels tab
  var levels = data.levels;
  var tbodyLevels = $('#levels-table').find('tbody');
  tbodyLevels.html('');
  for (var j = 0; j < levels.length; j++) {
    var level = levels[j];
    tbodyLevels.append('<tr class="linked"><td>' + (j + 1) + '</td><td class="words-per-sentence">' +  setLevelValue(level.maxWordsPerSentence) + '</td><td class="words-per-page">' +  setLevelValue(level.maxWordsPerPage) + '</td><td class="words-per-book">' +  setLevelValue(level.maxWordsPerBook) + '</td><td class="unique-words-per-book">' +  setLevelValue(level.maxUniqueWordsPerBook) + '</td><td style="display: none">' + level.thingsToRemember.join('\n') + '</td></tr>');
  }

  // click event for level rows
  tbodyLevels.find('tr').onOnce('click', function() {
    selectLevel(this);
  });
}

function saveClicked(): void {

  // update more words
  if ((<HTMLInputElement>document.getElementById('dls_more_words')).value !== previousMoreWords) {

    var accordion = accordionWindow();

    // save the changes and update lists
    saveChangedSettings(function() {
      if (typeof accordion['readerSampleFilesChanged'] === 'function')
        accordion['readerSampleFilesChanged']();
      parent.window['closeSetupDialog']();
    });
  }
  else {
    saveChangedSettings(parent.window['closeSetupDialog']);
  }
}

/**
 * Pass the settings to C# to be saved.
 * @param [callback]
 */
function saveChangedSettings(callback?: Function): void {

  // get the values
  var s = getChangedSettings();

  // send to parent
  var settingsStr = JSON.stringify(s, ReaderSettingsReplacer);
  accordionWindow().postMessage('Refresh\n' + settingsStr, '*');

  // save now
  if (callback)
    getIframeChannel().simpleAjaxPost('/bloom/readers/saveReaderToolSettings', callback, settingsStr);
  else
    getIframeChannel().simpleAjaxNoCallback('/bloom/readers/saveReaderToolSettings', settingsStr);
}

function getChangedSettings(): any {

  var s: ReaderSettings = new ReaderSettings();
  s.letters = cleanSpaceDelimitedList((<HTMLInputElement>document.getElementById('dls_letters')).value);

  // remove duplicates from the more words list
  var moreWords: string[] = _.uniq(((<HTMLInputElement>document.getElementById('dls_more_words')).value).split("\n"));

  // remove empty lines from the more words list
  moreWords = _.filter(moreWords, function(a: string) { return a.trim() !== ''; });
  s.moreWords = moreWords.join(' ');

  // stages
  var stages: JQuery = $('#stages-table').find('tbody tr');
  for (var i: number = 0; i < stages.length; i++) {
    var stage: ReaderStage = new ReaderStage((i + 1).toString());
    var row: HTMLTableRowElement = <HTMLTableRowElement>stages[i];
    stage.letters = (<HTMLTableCellElement>row.cells[1]).innerHTML;
    stage.sightWords = cleanSpaceDelimitedList((<HTMLTableCellElement>row.cells[2]).innerHTML);

    // do not save stage with no data
    if (stage.letters || stage.sightWords)
      s.stages.push(stage);
  }

  // levels
  var levels: JQuery = $('#levels-table').find('tbody tr');
  for (var j: number = 0; j < levels.length; j++) {
    var level: ReaderLevel = new ReaderLevel((j + 1).toString());
    var row: HTMLTableRowElement = <HTMLTableRowElement>levels[j];
    level.maxWordsPerSentence = getLevelValue((<HTMLTableCellElement>row.cells[1]).innerHTML);
    level.maxWordsPerPage = getLevelValue((<HTMLTableCellElement>row.cells[2]).innerHTML);
    level.maxWordsPerBook = getLevelValue((<HTMLTableCellElement>row.cells[3]).innerHTML);
    level.maxUniqueWordsPerBook = getLevelValue((<HTMLTableCellElement>row.cells[4]).innerHTML);
    level.thingsToRemember = (<HTMLTableCellElement>row.cells[5]).innerHTML.split('\n');
    s.levels.push(level);
  }
  return s;
}

function getLevelValue(innerHTML: string): number {

  innerHTML = innerHTML.trim();
  if (innerHTML === '-') return 0;
  return parseInt(innerHTML);
}

/**
 * if the user enters a comma-separated list, remove the commas before saving (this is a space-delimited list)
 * @param original
 * @returns {string}
 */
function cleanSpaceDelimitedList(original: string): string {

  var cleaned: string = original.replace(/,/g, ' '); // replace commas
  cleaned = cleaned.trim().replace(/ ( )+/g, ' ');   // remove consecutive spaces

  return cleaned;
}