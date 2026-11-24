const nextSelector = 'button[data-test-id="NEXT_TRACK_BUTTON"], button[data-test-id="MOBILE_NEXT_TRACK_BUTTON"]';
const nextLabels = new Set(["следующий трек", "next track"]);

function canClick(button) {
  return !button.matches(":disabled, [aria-disabled='true']") && button.getClientRects().length > 0;
}

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== chrome.runtime.id || message?.command !== "next") return;

  let button = Array.from(document.querySelectorAll(nextSelector)).find(canClick);
  if (!button) {
    button = Array.from(document.querySelectorAll("button[aria-label], button[title]")).find((candidate) =>
      canClick(candidate) && ["aria-label", "title"].some((attribute) =>
        nextLabels.has(candidate.getAttribute(attribute)?.trim().toLowerCase())
      )
    );
  }
  if (button) button.click();
  respond({ accepted: Boolean(button) });
});
