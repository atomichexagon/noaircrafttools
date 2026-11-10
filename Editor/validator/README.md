# Aircraft Validator
This is where the messes are made. Currently only validates aircraft, but if someone wants to
adapt it to other things, [feel free.](TODO.md)

I would apologize in advance for the code you're about to read, but I wouldn't mean it

## Where to find things
- Validator window: [AircraftValidator.cs](AircraftValidator.cs)
- Validator basic functionality: [ValidatorBase.cs](ValidatorBase.cs)
- Aircraft object validation (Definition, Params): [ObjectValidators.cs](ObjectValidators.cs)
- Aircraft prefab validation: [PrefabValidator.cs](PrefabValidator.cs)
	- Where most of the magic happens, checking the five billion different components aircraft need
- Cases I was too lazy to add myself but probably need to be checked: [TODO.md](TODO.md)

## Extending the validator
Add a class implementing 'IValidator' and mark dependencies (e.g. AircraftDefinitionValidator) with '[Requires(typeof(OtherValidator))]'.
Validators should be automatically discovered as long as they're in the right namespace.

Refer to other validators (such as those in [PrefabValidator](PrefabValidator.cs)) to see how to set up the expected results object.