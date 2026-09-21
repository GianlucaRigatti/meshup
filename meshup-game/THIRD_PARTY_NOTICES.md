# Third-party notices

## Ubiq sample UI

The menu, room-browser, keyboard, and key prefabs under
`Assets/Prefabs/Ubiq Sample UI` are copied from Ubiq 1.0.0-pre.16 by the UCL
Immersive Virtual Environments Laboratory. Ubiq is Copyright 2021 the Virtual
Environments and Computer Graphics Group, University College London, and is
licensed under the Apache License 2.0. The complete license text is stored at
`Assets/Prefabs/Ubiq Sample UI/LICENSE-Ubiq.txt`.

## Vosk API and Unity bindings

Vosk is Copyright 2019 Alpha Cephei Inc. and is distributed under the Apache
License 2.0. The complete license text is stored at
`Assets/ThirdParty/Vosk/LICENSE.txt`.

- C# bindings, Android ARM64, macOS universal, and Windows x64 binaries:
  `alphacep/vosk-unity-asr` commit
  `6cc1d5a2a2837e570e32eec4f0ada383a5e94d04`.
- Linux x64 binary: official `vosk-linux-x86_64-0.3.45.zip` release archive.
- Speech model: `vosk-model-small-en-us-0.15`, Apache License 2.0.

Upstream sources:

- https://github.com/alphacep/vosk-api
- https://github.com/alphacep/vosk-unity-asr
- https://alphacephei.com/vosk/models

SHA-256 checksums:

```text
30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498  vosk-model-small-en-us-0.15.zip
df34da99f07a0fb54e328ba6c0bad5545c646cde64769feaa6a76968c7618f64  Android64/libvosk.so
82fdfba0dde392a7dbba70f9c1a17f6e3da27a50444a9f53939508525a1e6fdf  OSX/libvosk.dylib
f0efe483a8207e11a70fc16eaa7d767a1f6d22bfe6b967ca5278e2bc7cc60e9c  Windows/libvosk.dll
0492b927d1ae02cebc0876b465760d0e352eb871fe0486cb1a4b7da3e82fd187  Windows/libgcc_s_seh-1.dll
73d3b4873fecf2686a1f6aca077bb3335a759ea64c63a0d2312586bf57ecb9a6  Windows/libstdc++-6.dll
5b60907df42009f1ba437e7f2a6278651be97f4993869105d8a31086138e4570  Windows/libwinpthread-1.dll
85c4654de3acdeb99abab86eeb2a6e603927d37089597c0fcc33d8638dc2ccaf  Linux/libvosk.so
```

## Holy Aura Resonance – Magical Energy Loop

“Holy Aura Resonance – Magical Energy Loop” by Tommaso Motteran
(Coghezzi), sourced from Freesound:
https://freesound.org/people/TommasoMotteran/sounds/853628/

Licensed under Creative Commons Attribution 4.0:
https://creativecommons.org/licenses/by/4.0/

The bundled `Assets/Resources/HolyAuraResonance.ogg` was converted to mono and
time-stretched to 90% speed for use as positional generator ambience.

## Success Notification

“Success Notification” by Universfield, sourced from Pixabay:
https://pixabay.com/sound-effects/film-special-effects-success-notification-132473/

Licensed under the Pixabay Content License:
https://pixabay.com/service/license-summary/

The bundled `Assets/Resources/SuccessNotification.wav` was converted to mono
PCM for reliable Unity playback and is used as the positional cue when a
player guesses the mime word correctly.
