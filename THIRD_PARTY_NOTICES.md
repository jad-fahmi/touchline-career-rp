# Third-party notices

The native, read-only FIFA 18 career-save parser uses a focused subset of table and field metadata derived from:

- `fifa-career-save-parser`, copyright Sammy Griffiths, distributed under the ISC license: https://github.com/sammygriffiths/fifa-career-save-parser
- FIFA career database extraction and research by xAranaktu: https://gist.github.com/xAranaktu/96e40cb2287372bbbe405408485a87e4

The implementation in this repository is a C# port limited to reading the fields needed to normalize a career snapshot. It does not contain Cheat Engine scripts, memory offsets, save-writing code, or game assets.

The embedded offline player-name index contains only FIFA player ID, display name, and nationality fields reduced from the public FIFA 18 `CompleteDataset.csv` maintained by the `fifa-players/Fifa` project:

- Source: https://github.com/fifa-players/Fifa/blob/f28707bcef26c2e65b71111eec09cceb22666728/CompleteDataset.csv
- The source project describes the dataset as FIFA 18 player data and attributes its underlying records to SoFIFA.

No photos, logos, performance attributes, financial values, or other dataset columns are included. Edited and generated-player names are read directly from the user's save instead.
