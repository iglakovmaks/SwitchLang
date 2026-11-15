import { chromium } from 'playwright';
import { spawn, execFileSync } from 'node:child_process';
import { resolve } from 'node:path';

const app = spawn(resolve(process.argv[2]), ['--background'], { stdio: 'ignore' });
const browser = await chromium.launch({ channel: 'msedge', headless: false });
const page = await browser.newPage();
try {
  await page.setContent('<textarea id="editor" style="width:600px;height:300px"></textarea>');
  await page.waitForTimeout(1500);
  await page.bringToFront();
  const input = page.locator('#editor');
  await input.click();
  setLayout('00000409');
  await page.waitForTimeout(700);
  await input.fill('ghbdtn');
  await input.press('Control+a');
  setLayout('00000419');
  await page.waitForFunction(() => document.querySelector('#editor').value === 'привет', null, { timeout: 5000 });
  console.log('PASS: Edge textarea English → Russian');
  setLayout('00000409');
  await page.waitForFunction(() => document.querySelector('#editor').value === 'ghbdtn', null, { timeout: 5000 });
  console.log('PASS: Edge textarea Russian → English');
} catch (error) {
  console.error('Editor state:', await page.locator('#editor').evaluate(e => ({value:e.value,start:e.selectionStart,end:e.selectionEnd})));
  await page.screenshot({ path: 'artifacts/browser-failure.png' });
  throw error;
} finally {
  app.kill();
  await browser.close();
}

function setLayout(name) {
  const code = `Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class Keyboard {
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr LoadKeyboardLayout(string name, uint flags);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
'@
$h = [Keyboard]::GetForegroundWindow()
$layout = [Keyboard]::LoadKeyboardLayout('${name}', 0)
[Keyboard]::PostMessage($h, 0x0050, [IntPtr]::Zero, $layout)
`;
  execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', code], { windowsHide: true });
}
