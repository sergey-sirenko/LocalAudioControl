const hostName = "local_audio_control.yandex_music";
const reconnectAlarm = "reconnect";
let nativePort;

function connect() {
  if (nativePort) return;
  const port = chrome.runtime.connectNative(hostName);
  nativePort = port;
  port.onDisconnect.addListener(() => {
    // Reading lastError suppresses Chrome's unchecked-error message during setup.
    if (chrome.runtime.lastError) console.debug(chrome.runtime.lastError.message);
    nativePort = undefined;
    chrome.alarms.create(reconnectAlarm, { delayInMinutes: 0.5 });
  });
  port.onMessage.addListener(async (message) => {
    if (message?.command !== "next") return;
    let result = "error";
    try {
      const query = { url: "https://music.yandex.ru/*", discarded: false };
      if (message.playingOnly === true) query.audible = true;
      const tabs = await chrome.tabs.query(query);
      tabs.sort((a, b) => Number(Boolean(b.audible)) - Number(Boolean(a.audible)) ||
        (b.lastAccessed ?? 0) - (a.lastAccessed ?? 0));
      if (!tabs.length) {
        result = "no-tab";
      } else {
        const reply = await chrome.tabs.sendMessage(tabs[0].id, { command: "next" }, { frameId: 0 });
        result = reply?.accepted === true ? "accepted" : "no-button";
      }
    } catch (error) {
      console.debug("Yandex Music command failed", error);
    }
    if (nativePort === port) port.postMessage({ result });
  });
}

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === reconnectAlarm) connect();
});
chrome.runtime.onStartup.addListener(connect);
chrome.runtime.onInstalled.addListener(connect);
connect();
