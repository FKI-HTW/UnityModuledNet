# UnityModuledNet


## Overview

The Unity project that holds the package and the project configuration is on the `main` branch. </br>
The version of the package that can be used to import it into Unity is on the `upm` branch. </br>
The files for the documentation can be found on the `docs` branch.

## Installation and user guide

To import this package into Unity the import via URL can be used.</br>
For example: `https://github.com/CENTIS-HTW/UnityModuledNet.git#<version>` </br>

For further details, please refer to [this readme](Assets/UnityModuledNet/README.md) inside the package.

## Developing for this package

This repository uses a subtree for the Unity Package.
That way the valid Unity Package and the development Unity project that contains it can be placed into one repository.

The development process stays the same, while the release of a new version of the package takes place on the `upm` branch.
You can find further details [here](https://www.patreon.com/posts/25070968)

### Pushing the Unity package to the upm branch

**IMPORTANT**</br>
Check that the `Modules` folder located under `Assets/` is renamed to `Modules~` before creating a new release!
Otherwise Unity will throw an error stating that the contained scripts can't be compiled.
This practice follows the Unity guidelines for creating the package structure.
This only has to be done for the versions on the upm branch!

Before you start, check that the version of the package under `Assets/UnityModuledNet/package.json` is correct.
Also check that `Assets/UnityModuledNet/CHANGELOG.md` is updated, to reflect the changes made.

Note that `"version"` needs to be replaced by the version number that you want to release.
```
git subtree split --prefix=Assets/UnityModuledNet --branch upm
git tag "version" upm
git push origin upm --tags
```

To delete a wrong tag:
```
git tag -d tagName
```
If the wrong tag is already pushed:
```
git push origin :tagName
```