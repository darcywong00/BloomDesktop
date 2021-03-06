/// <reference path="interIframeChannel.ts" />
/**
 * Finds the interIframeChannel on the main document
 */
function getIframeChannel(): interIframeChannel {

  if (typeof document["interIframeChannel"] === 'object') {
    return document["interIframeChannel"];
  }
  else if (typeof window.parent["interIframeChannel"] === 'object') {
    return window.parent["interIframeChannel"];
  }

  // not found
  return null;
}
