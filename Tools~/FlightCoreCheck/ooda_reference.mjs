// Runs the OODA tool's own point-mass step with its T-6A set, for comparison with the C# port.
// Usage: node ooda_reference.mjs <path to dads-debreif/src/core>
const core = process.argv[2];
const { stepPointMass, pointMassState } = await import(`${core}/point-mass.js`);
const { t6aExcessFn } = await import(`${core}/t6-performance.js`);
const run = (name, ktas, g, bankDeg, secs) => {
  let s = pointMassState({ altFt: 5000, ktas, headingRad: Math.PI / 2 });
  for (let i = 0; i < Math.round(secs / 0.02); i++) s = stepPointMass(s, { g, bankRad: bankDeg * Math.PI / 180 }, 0.02, t6aExcessFn);
  console.log(`${name} pos ${s.x.toFixed(2)} ${s.y.toFixed(2)} ${s.z.toFixed(2)} vel ${s.vx.toFixed(3)} ${s.vy.toFixed(3)} ${s.vz.toFixed(3)}`);
};
run('level', 200, 1, 0, 30);
run('loop', 250, 4, 0, 8);
run('turn', 250, 3, 70.5288, 20);
run('climbturn', 250, 2, 45, 15);
