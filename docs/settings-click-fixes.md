# Settings click regression, 2026.8.18 Itch

Mod settings rows use synthetic data with `Title = Accept`; it is not a native
game option identifier. Native click methods can bypass a nested `UpdateValue`
hook through inlining. The fixed Toggle/Increase/Decrease entry prefixes route
registered mod rows through their managed setter directly, preserve host editing
permissions and notify the owner exactly once. Vanilla rows retain their native
path. The same-date Steam inline dataset is comparison evidence, not proof of
Itch machine-code behavior.

Cold-start testing also exposed a null template in the preset sidebar: global
`GameObject.Find("ModeValue")` depended on another active menu. The sidebar now
uses its own native prefab references and creates a display-only fallback when
the old ModeValue object is absent. It does not clone an unbound OptionBehaviour.

Validation: 60 extracted-production routing assertions and a real Itch LocalGame
test passed. Native Toggle/Increase/Decrease modified and restored one boolean,
one string and one floating-point setting; each action raised exactly one option
update event and the displayed values agreed. The test also exercised three menu
owners, interrupted tab construction, complete tab traversals and cleanup, with
zero Error/Fatal logs. Numeric display checks wait for native FixedUpdate, not
only two render frames. This is native-handler coverage, not physical mouse input
coverage. The pre-test Options.json was restored byte-for-byte after process exit.

The accompanying lobby fix suppresses obsolete PlayerControl SyncSettings RPC2:
the 2026.8.18 receiver no longer handles it and falls into role dispatch before a
lobby player has a Role. Host option changes now mark LogicOptions dirty and use
the existing current-options component sender. Its 65 regression assertions pass;
two matching Itch clients subsequently joined, started, voted and completed an
exile without the previous native null reference or any Error/Fatal log.
