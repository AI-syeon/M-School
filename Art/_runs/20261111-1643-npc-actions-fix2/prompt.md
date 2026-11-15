# NPC actions fix round 2

Preserve everything except the four requested corrections. Read original spec Art/_runs/20261111-1622-npc-actions/prompt.md and round1 prompt.md + last.txt.

1. eat: replace unreadable bright bread with deterministic pixel-drawn round roll about 5x4 pixels, three toasted tan golden tones, one highlight pixel and 1px dark outline. Visible 2x2 or 2x3 skin hand under/beside roll with sleeve cuff above. Frame0 chest; frame1 below mouth; frame2 at mouth with 1px bitten notch, open mouth and cheek one skin pixel wider; frame3 halfway lowered, larger notch and closed chewing mouth. Sleeve forearm may be drawn if generated arm is unclean. Less saturated bread, lighter than coat.
2. read/write closed mouth as idle, lowered eyes unchanged. Book/notebook/pen and motion unchanged.
3. Non-prop pixels of ALL action frames exclusively idle/walk palette. Extra colors only actual prop masks. Assert this in build_characters.py and fail build on violation. Correct saturated coat trim/buttons especially Serin.
4. chat only mouth changes: frames1/3 clear 2x2 dark open mouth and lighter skin pixel row above; frames0/2 closed as idle. Preserve chat arms/hands.

Keep eight originals and prompt JSON unchanged, filenames, manifest format/order/fps, rectangles/layout, mirror, baseline 120 runtime+mirror frames and atlas regions, player bytes, idle-derived heads, book/notebook props/motion, chat arm poses. No new source art needed: this round explicitly draws bread deterministically from pixels in recipe and preserves sources.

Regenerate all reviews including npc-actions-compare-idle.png 6x and verification.json. Run --verify-rebuild twice with identical results; compare original baseline; check_png.py --kind any on all new PNGs. Open and inspect 6x and 1x. Final per-problem results and remaining weaknesses.

Allowed writes only Art/Characters/actions.json, build_characters.py, review/; Art/_runs/; generated Assets/MagicCampus/Art/Characters/{npc-serin,npc-mira,player} and Assets/StreamingAssets/UiArt/Characters/{npc-serin,npc-mira,player} through build script only. No git, paid external APIs, C# or other scene/design edits. Never overwrite sources. Only Python C:/Users/syeon/Workspace/2D-pixel-asset-pipeline/.venv/Scripts/python.exe; no changes inside pipeline. 32x32 tile grid, characters 32PPU point filter. Warm muted coherent clustered pixels, no blur or cast shadows. Run notes folder is this folder.
