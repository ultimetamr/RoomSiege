# Third-party dependencies and publication scope

## PICO Unity Integration SDK 6.1.1

The locally installed SDK's `LICENSE.md` reserves PICO's rights and restricts dissemination/reproduction without prior written permission. The SDK and copied native libraries are therefore **not distributed in this repository**. Obtain the official SDK yourself under its own terms, then run `Tools/Setup-Dependencies.ps1` before opening Unity.

The setup script copies the SDK into a local Git-ignored directory and applies narrowly scoped compatibility changes needed by this project. It restores two audio plugin aliases from the same SDK. It does not grant rights to redistribute any SDK content.

SDK-generated PICO debugger prefabs/actions/configuration copies are excluded; the installed SDK regenerates them.

## Unity packages

Unity, URP, OpenXR, XR Interaction Toolkit, Input System, Composition Layers and other packages remain governed by their respective licenses and terms. Registry dependencies resolve through Unity Package Manager. The local Composition Layers archive is restored from the user's installed Unity editor and is not republished here.

The project includes its Unity template-derived configuration and template readme helper scripts. No blanket relicensing of these materials is asserted.

## Project art and branding

RoomSiege includes procedural geometry, model-generation scripts, UI assets and generated promotional art. The poster and existing key art are **concept illustrations, not captured gameplay**. The RoomSiege icon is the project's custom cyan crystal emblem. No third-party trademark endorsement is implied.

## Source availability

Making this source repository public does not itself grant an unrestricted license. No repository-wide open-source license has been selected by the owner. Contact the repository owner before redistribution or commercial reuse, and obtain independent licenses for third-party dependencies.
