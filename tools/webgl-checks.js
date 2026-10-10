// Shared browser-test checks for the WebGL smoke scenarios (required by webgl-drive.js and webgl-worldmap.js).

// ---- shared checks on what the game itself measured ([Render] and [Perf] lines come from RenderingRig / PerfProbe)
function renderAndPerfChecks(logs, failures, label) {
  const render = logs.find(l => /^\[Render\] tier=/.test(l));
  console.log(label + ' render: ' + (render || 'NO [Render] LINE').slice(0, 330));
  if (!render) failures.push('no [Render] line: the rendering foundation did not initialise');
  else if (/pipeline=NONE/.test(render)) failures.push('no render pipeline asset is active');
  const perf = logs.filter(l => /^\[Perf\] tier=/.test(l));
  console.log(label + ' perf lines: ' + perf.length); perf.slice(-3).forEach(l => console.log('  ' + l.slice(0, 420)));
  if (perf.length === 0) failures.push('no [Perf] measurement was logged');
  else {
    const last = perf[perf.length - 1], num = k => { const m = new RegExp(k + '=([\\d.]+)').exec(last); return m ? +m[1] : NaN; };
    if (!(num('visibleTris') > 1000)) failures.push('implausible visible triangle count: ' + num('visibleTris'));
    if (!(num('chunks') >= 5)) failures.push('fewer than 5 world chunks loaded: ' + num('chunks'));
  }
  const over = logs.filter(l => /\[Perf\] BUDGET EXCEEDED/.test(l)); over.slice(0, 5).forEach(l => console.log('  ' + l.slice(0, 300)));
  if (over.length) failures.push(over.length + ' performance budget violation(s), first: ' + over[0].slice(0, 200));
  const bad = logs.filter(l => /\[World\].*failed|\[Route\] (invalid|could not)|\[Props\] invalid/.test(l));
  if (bad.length) failures.push('world generation reported: ' + bad[0].slice(0, 250));
  const route = logs.find(l => /^\[Route\] ng-lagos-ibadan:/.test(l)); console.log(label + ' route: ' + (route || 'NO [Route] LINE'));
  if (!route) failures.push('the route was not built from data (no [Route] summary line)');
}
module.exports = { renderAndPerfChecks };
