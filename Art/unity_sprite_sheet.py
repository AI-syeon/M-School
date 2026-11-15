"""Write Unity texture import metadata for sprites on a pixel grid."""

import hashlib
import re
import uuid


def write_sprite_meta(image, size, pixels_per_unit, frames, pivot=(0.5, 0.5), normal=None):
    path = image.with_suffix(".png.meta")
    original = path.read_text(encoding="utf-8-sig") if path.exists() else ""
    guid_match = re.search(r"^guid: ([0-9a-f]{32})$", original, re.MULTILINE)
    guid = guid_match[1] if guid_match else uuid.uuid4().hex
    multiple = len(frames) > 1
    sprites, names, identifiers = [], [], []
    for name, x, y, w, h in frames if multiple else ():
        digest = hashlib.sha256(f"{guid}/{name}".encode()).digest()
        sprite_id = digest[:16].hex()
        file_id = int.from_bytes(digest[16:24], "big", signed=True)
        sprites.append(f"""    - serializedVersion: 2
      name: {name}
      rect:
        serializedVersion: 2
        x: {x}
        y: {size[1] - y - h}
        width: {w}
        height: {h}
      alignment: 9
      pivot: {{x: {pivot[0]}, y: {pivot[1]}}}
      spriteID: {sprite_id}
      internalID: {file_id}""")
        names.append(f"      {name}: {file_id}")
        identifiers.append(f"  - first:\n      213: {file_id}\n    second: {name}")
    sprites_yaml = "\n" + "\n".join(sprites) if sprites else " []"
    names_yaml = "\n" + "\n".join(names) if names else " {}"
    identifiers_yaml = "\n" + "\n".join(identifiers) if identifiers else " []"
    secondary = " []"
    if normal:
        normal_guid = write_normal_meta(normal, size)
        secondary = f"\n    - texture: {{fileID: 2800000, guid: {normal_guid}, type: 3}}\n      name: _NormalMap"
    max_size = max(32, 1 << (max(size) - 1).bit_length())
    result = f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable:{identifiers_yaml}
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 1
  isReadable: 0
  maxTextureSize: {max_size}
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 1
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  spriteMode: {2 if multiple else 1}
  spriteMeshType: 0
  alignment: 9
  spritePivot: {{x: {pivot[0]}, y: {pivot[1]}}}
  spritePixelsToUnits: {pixels_per_unit}
  alphaUsage: 1
  alphaIsTransparency: 0
  textureType: 8
  textureShape: 1
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {max_size}
    textureFormat: -1
    textureCompression: 0
  spriteSheet:
    serializedVersion: 2
    sprites:{sprites_yaml}
    secondaryTextures:{secondary}
    nameFileIdTable:{names_yaml}
"""
    if result != original:
        path.write_text(result, encoding="utf-8")


def write_normal_meta(image, size):
    path = image.with_suffix(".png.meta")
    original = path.read_text(encoding="utf-8-sig") if path.exists() else ""
    match = re.search(r"^guid: ([0-9a-f]{32})$", original, re.MULTILINE)
    guid = match[1] if match else uuid.uuid4().hex
    max_size = max(32, 1 << (max(size) - 1).bit_length())
    result = f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  serializedVersion: 13
  mipmaps:
    enableMipMap: 0
    sRGBTexture: 0
  maxTextureSize: {max_size}
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 1
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  alphaUsage: 1
  textureType: 0
  textureShape: 1
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: {max_size}
    textureFormat: -1
    textureCompression: 0
"""
    if result != original:
        path.write_text(result, encoding="utf-8")
    return guid
