# Nuclear Option Aircraft Tools
Tools for evaluating aircraft for use in Nuclear Option.
This will not build your mod - for that you'll need [Blueprinter](https://github.com/nikkorap/NOBlueprinter-Editor).

## Features
- Aircraft validator, to check the game's many footguns before ever building the mod
	- Currently only checks basic cases, e.g. Definition, Parameters, AeroParts
- Wind tunnel, to analyze an aircraft's behavior in the editor

## Requirements
- Unity 2022.3.62f2
- Blueprinter (see link above, set that up before doing anything here)

## Installation
In a Unity project where Blueprinter has already been installed:

1. In topbar: *Window -> Package Manager*
2. Click *+ > Add package from git URL...*
3. Point it to `https://github.com/atomichexagon/noaircrafttools.git`

## Usage

### Validator
1. Open *Aircraft Tools > Check aircraft*
2. Select your aircraft definition
3. Click *Validate*

Results are grouped by category (e.g. AeroPart) and severity (Info, Warn, Fail, Critical).
Click on the object's name (shows up as a button) to jump to the object returning a message.

Several warnings/messages offer automatic fix options to correct/set things for you, you can use these
by clicking the buttons under Fix if available.

The validator doesn't currently cover most cases that can cause errors. If you run into something it doesn't cover,
please feel free to open an issue or post it in the thread in Nuclear Option Modding Forum (under Tools)!

If you wish to contribute to the validator, please see [the Validator's README](Editor/validator/README.md)


### Wind Tunnel
1. Open *Aircraft Tools > Wind tunnel*
2. Select your aircraft
3. Choose either the wind tunnel scene (all features) or Prefab scene
	- Just use the wind tunnel scene in most cases. Prefab mode should probably be removed
4. Select desired mode, change settings, press Simulate
5. Click on results to show/hide certain outputs
6. Admire the pretty colors

This tool can show you how your aircraft will perform in-game in various different cases.
There's currently no documentation on how to interpret each graph, but for starters:
- Torque series
	- Positive pitch torque means the aircraft wants to pitch down
	- Positive roll torque means the aircraft wants to roll left
	- Positive yaw torque means the aircraft wants to yaw right
- Coefficients
	- Cl is coefficient of lift
	- Cd is coefficient of drag (might be a bit off currently)

Known issues:
- Causing a domain reload while in the Visualizer (wind tunnel only) can leave
	the aircraft model rotated. Reset and Rebuild to fix. Doesn't affect the original prefab.


## Contributions
Contributions are very welcome, especially to the validator. There are many, many TODOs shotgunned around the codebase,
so feel free to tackle any of those.

For major changes, preferably reach out (see below) or open an issue to make sure it'll fit nicely, especially if it's
changing things that already exist (e.g. restructuring wind tunnel analysis modes).

You can get in contact via the [Nuclear Option modding forums (discord server)](https://discord.gg/P5uYCF2Xe), in the
dedicated thread under Tools (NO Aircraft Tools), Discord DMs @hexagonest, or open an issue here.

### Validator coverage requests
If the validator doesn't cover an issue that you've run into, feel free to open an issue or get in contact (see above).
The validator will probably never catch *everything*, but coverage can always improve.

### Bug reports
Please include Unity version, Unity log, and whatever you were doing at the time.

### AI contributions
Use whatever tools you'd like for programming, but keep it reasonable. PRs should be focused and sensible, and you
should be able to explain what you changed and why. Poorly explained or overly broad PRs probably won't be merged.


## License
MIT, see [License](LICENSE)


## Credits
- [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/) by Shockfront Studios
- [Blueprinter](https://github.com/nikkorap/NOBlueprinter-Editor) by Nikkorap

*This project is an unofficial community modification and is not affiliated with, sponsored by, or endorsed by Shockfront Studios Pty Ltd. Nuclear Option and Shockfront Studios are trademarks or registered trademarks of Shockfront Studios Pty Ltd.*