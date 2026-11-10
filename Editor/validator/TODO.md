# Validator

#### General features

- Adapting to other use cases (e.g. GroundUnit, Missile, whatever)
	- If someone wants to do this, we should try to find a decent way to structure it.
		e.g. split the file structure up into validator/Aircraft, validator/GroundUnit, etc.
	- Ideally it should stay under one window, with a way to pick between types (probably defaulting to Aircraft)



## Aircraft

### Definition

#### Liveries
- Check the Livery object, make sure it's addressable
	- For anyone who knows more about this - it'd be awesome if there was any documentation whatsoever on how Addressable livery assets *should* be done.
	- I have no familiarity with Addressables, so figuring this out the first time was agonizing, and I'm probably still doing it wrong

### Prefab

#### miscellaneous
- CargoRamp origin should be at the pivot point (how do we even check this?)
- CargoRamp parent in Unity should be the part it's mounted to (i think?)

#### LandingGear
- *gestures vaguely at everything*
- Should probably check positions and orientations better, the current setup is meh and doesn't catch a lot of things.

#### Aircraft
- CameraTransform null check (critical)

#### AeroParts
- Collider material (does it actually *have* to be Metal?)
- Collider Convex toggle
- Colliders being null (this took over an hour to figure out lmao)
	- This silently prevents AeroParts from working, resulting in not generating lift or drag

#### ConstantSpeedProp
- Blade forwards should be facing OUT
	- As in, the blade transform selected in Blades should be facing radially outwards from the hub
- LiftNormal should be facing in the correct direction (hatred)
	- Forwards along spin direction, Up in the direction of desired thrust. Right doesn't seem to matter
- LiftNormal should be FORWARDS of the blade origin (locally! hatred)