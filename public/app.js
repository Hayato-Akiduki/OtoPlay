const $ = id => document.getElementById(id);
const audio = $('audio');
let ready = false;
let selectedDevice = '';
let currentName = '';
const tracks = [];
audio.volume = 0.7;
const status = message => { $('status').textContent = message; };
function stop() {
  audio.pause();
  if (audio.readyState) audio.currentTime = 0;
  $('now').textContent = '再生待ち';
}
function fail(error) {
  const messages = {
    NotAllowedError: 'デバイスへのアクセスが許可されませんでした。ブラウザーのサイト設定を確認してください。',
    NotFoundError: '出力デバイスが見つかりません。接続・VB-CABLE のインストールを確認してください。',
    NotSupportedError: 'この音声形式または出力切り替えに対応していません。別のファイルやブラウザーで試してください。',
    AbortError: '出力の切り替えが中断されました。デバイスを再読み込みしてください。',
  };
  status(messages[error.name] || `処理に失敗しました: ${error.message}`);
}
async function refreshDevices() {
  const devices = (await navigator.mediaDevices.enumerateDevices()).filter(d => d.kind === 'audiooutput');
  const select = $('output');
  select.replaceChildren(new Option('出力先を選択してください', ''));
  for (const d of devices) select.add(new Option(d.label || `出力デバイス ${select.options.length}`, d.deviceId));
  select.disabled = false;
  select.value = selectedDevice;
  if (selectedDevice && !devices.some(d => d.deviceId === selectedDevice)) {
    stop(); ready = false; selectedDevice = '';
    status('出力デバイスが外れました。送り先を選び直してください。');
  }
  return devices;
}
$('devices').onclick = async () => {
  $('devices').disabled = true;
  try {
    if (!audio.setSinkId || !navigator.mediaDevices) throw new DOMException('出力切り替えに対応した Chrome / Edge で開いてください。', 'NotSupportedError');
    if (navigator.mediaDevices.selectAudioOutput) {
      const device = await navigator.mediaDevices.selectAudioOutput();
      await refreshDevices();
      $('output').value = device.deviceId;
      await setOutput(device.deviceId);
    } else {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      stream.getTracks().forEach(track => track.stop());
      const devices = await refreshDevices();
      status(devices.some(d => /cable.*input/i.test(d.label))
        ? 'CABLE Input が見つかりました。出力先から選択してください。'
        : '出力先を選択してください。CABLE Input がない場合は VB-CABLE のインストール後に再読み込みしてください。');
    }
  } catch (error) { fail(error); }
  finally { $('devices').disabled = false; }
};
async function setOutput(id) {
  stop(); ready = false;
  if (!id) { selectedDevice = ''; status('出力先を選択してください。'); return; }
  try {
    await audio.setSinkId(id);
    selectedDevice = id; ready = true;
    status(`出力先: ${$('output').selectedOptions[0].textContent}`);
  } catch (error) { selectedDevice = ''; $('output').value = ''; fail(error); }
}
$('output').onchange = () => setOutput($('output').value);
$('files').onchange = () => {
  if (!tracks.length) $('tracks').replaceChildren();
  for (const file of $('files').files) {
    const track = { name: file.name, url: URL.createObjectURL(file) };
    tracks.push(track);
    const row = document.createElement('div'); row.className = 'track';
    const name = document.createElement('span'); name.textContent = track.name;
    const play = document.createElement('button'); play.textContent = '▶ 再生';
    play.onclick = async () => {
      if (!ready) { status('先に出力先を選択してください。'); return; }
      stop(); audio.src = track.url; currentName = track.name;
      try { await audio.play(); $('now').textContent = `再生中: ${track.name}`; }
      catch (error) { fail(error); }
    };
    row.append(name, play); $('tracks').append(row);
  }
  $('stop').disabled = tracks.length === 0;
  $('files').value = '';
};
$('stop').onclick = stop;
$('volume').oninput = () => { audio.volume = Number($('volume').value) / 100; $('volumeValue').textContent = `${$('volume').value}%`; };
$('loop').onchange = () => { audio.loop = $('loop').checked; };
audio.onended = () => { $('now').textContent = `再生終了: ${currentName}`; };
audio.onerror = () => { $('now').textContent = '再生できません'; status('音声を読み込めませんでした。ブラウザー対応の音声ファイルを選んでください。'); };
if (navigator.mediaDevices) navigator.mediaDevices.addEventListener('devicechange', () => {
  if (!$('output').disabled) refreshDevices().catch(fail);
});
window.addEventListener('pagehide', () => { stop(); tracks.forEach(t => URL.revokeObjectURL(t.url)); });
