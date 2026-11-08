#!/usr/bin/env python3
"""
Spacebox to Corneroids Block Texture Mapping Harness

Compares Spacebox individual block texture PNGs pixel-by-pixel against
the original Corneroids block texture atlas (blockTextures.png) with grid
coordinates defined in DefaultBlocks.xml and defaultItemset.xml.

Features:
- Pixel-by-pixel RGBA comparison across all atlas grid cells (16x16 of 32x32 px)
- 4-way rotation invariance (0, 90, 180, 270 degrees)
- Similarity index calculation for mismatched textures (0.0 to 1.0 scale)
- Context-aware category matching using Spacebox folder structure
- Normalization of additional block states (e.g. wireAngle/wireHub -> wireTop)
- Generation of C# mapping dictionaries for SpaceboxStructureBridge.cs
- Detailed Markdown and JSON report generation
"""

import os
import sys
import json
import argparse
import xml.etree.ElementTree as ET
from pathlib import Path
from PIL import Image
import numpy as np

# State normalizations: map complex/variant states back to basic/canonical state
STATE_NORMALIZATIONS = {
    "wireangle": "wiretop",
    "wireangleoff": "wiretopoff",
    "wirehub": "wiretop",
    "wirehuboff": "wiretopoff",
    "wirestraight": "wiretop",
    "wirestraightoff": "wiretopoff",
    "wire": "wiretop",
    "wireoff": "wiretopoff",
    "port": "wiretop",
    "portoff": "wiretopoff",
    "straight": "wiretop",
    "straightoff": "wiretopoff",
    "angle": "wiretop",
    "angleoff": "wiretopoff",
    "hub": "wiretop",
    "huboff": "wiretopoff",
}

class BlockMappingHarness:
    def __init__(self, atlas_path: Path, itemset_path: Path, spacebox_blocks_dir: Path):
        self.atlas_path = atlas_path
        self.itemset_path = itemset_path
        self.spacebox_blocks_dir = spacebox_blocks_dir
        
        self.atlas_img = None
        self.atlas_cells = {}
        self.corneroids_blocks = {}
        self.cell_to_blocks = {}
        self.all_spacebox_pngs = {}
        
    def load_atlas(self, cell_size: int = 32):
        if not self.atlas_path.exists():
            raise FileNotFoundError(f"Corneroids atlas not found: {self.atlas_path}")
            
        self.atlas_img = Image.open(self.atlas_path).convert("RGBA")
        cols = self.atlas_img.width // cell_size
        rows = self.atlas_img.height // cell_size
        
        for cy in range(rows):
            for cx in range(cols):
                crop = self.atlas_img.crop((cx * cell_size, cy * cell_size, (cx + 1) * cell_size, (cy + 1) * cell_size))
                self.atlas_cells[(cx, cy)] = np.array(crop)

    def load_itemset(self):
        if not self.itemset_path.exists():
            raise FileNotFoundError(f"Corneroids itemset not found: {self.itemset_path}")
            
        tree = ET.parse(self.itemset_path)
        root = tree.getroot()
        
        for elem in root.iter():
            if "blockId" in elem.attrib:
                bid = int(elem.attrib["blockId"])
                name = elem.findtext("name", "")
                cat = elem.tag
                textures = {}
                for state in ("activeTextures", "inactiveTextures"):
                    st_elem = elem.find(state)
                    if st_elem is not None:
                        textures[state] = {}
                        for face in st_elem:
                            try:
                                fx = int(face.attrib["x"])
                                fy = int(face.attrib["y"])
                                fw = int(face.attrib.get("w", "1"))
                                fh = int(face.attrib.get("h", "1"))
                                textures[state][face.tag] = (fx, fy, fw, fh)
                                for dx in range(fw):
                                    for dy in range(fh):
                                        c = (fx + dx, fy + dy)
                                        if c not in self.cell_to_blocks:
                                            self.cell_to_blocks[c] = []
                                        self.cell_to_blocks[c].append((bid, name, cat, face.tag))
                            except (KeyError, ValueError):
                                pass
                                
                self.corneroids_blocks[bid] = {
                    "id": bid,
                    "name": name,
                    "category": cat,
                    "textures": textures
                }

    def index_spacebox_textures(self):
        if not self.spacebox_blocks_dir.exists():
            raise FileNotFoundError(f"Spacebox blocks directory not found: {self.spacebox_blocks_dir}")
            
        for p in self.spacebox_blocks_dir.rglob("*.png"):
            if "Emissions" in p.parts:
                continue
            self.all_spacebox_pngs[p.stem.lower()] = p

    def resolve_texture_file(self, tex_name: str, block_folder: Path) -> Path | None:
        raw_name = tex_name.lower().strip()
        # 1. Check state normalization
        name_to_search = STATE_NORMALIZATIONS.get(raw_name, raw_name)
        
        # 2. Local folder candidates
        candidates = [
            block_folder / f"{tex_name}.png",
            block_folder / f"{name_to_search}.png",
            block_folder.parent / f"{tex_name}.png",
            block_folder.parent / f"{name_to_search}.png",
        ]
        for cand in candidates:
            if cand.exists():
                return cand
                
        # 3. Global index
        if name_to_search in self.all_spacebox_pngs:
            return self.all_spacebox_pngs[name_to_search]
        if raw_name in self.all_spacebox_pngs:
            return self.all_spacebox_pngs[raw_name]
            
        return None

    def compare_texture(self, png_path: Path):
        try:
            img = Image.open(png_path).convert("RGBA")
        except Exception:
            return None
            
        orig_size = img.size
        # Resize to 32x32 if multiblock or non-standard
        if orig_size != (32, 32):
            img_32 = img.resize((32, 32), Image.Resampling.NEAREST)
        else:
            img_32 = img
            
        best_score = -1.0
        best_cell = None
        best_rot = 0
        max_diff = 255 * 32 * 32 * 4  # 4 RGBA channels
        
        for rot in (0, 90, 180, 270):
            rotated = img_32.rotate(rot) if rot != 0 else img_32
            arr = np.array(rotated)
            for (cx, cy), cell_arr in self.atlas_cells.items():
                diff = np.abs(cell_arr.astype(int) - arr.astype(int)).sum()
                score = 1.0 - (diff / max_diff)
                if score > best_score:
                    best_score = score
                    best_cell = (cx, cy)
                    best_rot = rot
                    if diff == 0:
                        return best_cell, best_score, best_rot, orig_size
                        
        return best_cell, best_score, best_rot, orig_size

    def map_all_blocks(self):
        self.load_atlas()
        self.load_itemset()
        self.index_spacebox_textures()
        
        block_jsons = sorted([p for p in self.spacebox_blocks_dir.rglob("*.json")
                             if p.name not in ("order.json", "blueprint.json")])
                             
        mappings = []
        for jpath in block_jsons:
            try:
                with open(jpath, "r", encoding="utf-8") as f:
                    data = json.load(f)
            except Exception:
                continue
                
            sbid = data.get("ID", data.get("Id", jpath.stem))
            if not sbid:
                continue
            sbname = data.get("Name", "")
            folder = jpath.parent
            cat = folder.parent.name if folder.parent != self.spacebox_blocks_dir else folder.name
            
            # Extract textures to compare
            tex_entries = {}
            tex_dict = data.get("Textures", {})
            if isinstance(tex_dict, dict):
                for state in ("Active", "Inactive"):
                    st = tex_dict.get(state)
                    if isinstance(st, dict):
                        for face, tn in st.items():
                            if isinstance(tn, str):
                                tex_entries[f"{state}.{face}"] = tn
                    elif isinstance(st, str):
                        tex_entries[state] = st
                        
            if "Cable" in data and isinstance(data["Cable"], dict):
                for sub, tn in data["Cable"].items():
                    if isinstance(tn, str):
                        tex_entries[f"Cable.{sub}"] = tn
                        
            # Perform comparisons
            matched_textures = []
            score_by_bid = {}
            
            for face_key, tex_name in tex_entries.items():
                png_file = self.resolve_texture_file(tex_name, folder)
                if not png_file:
                    continue
                match = self.compare_texture(png_file)
                if not match:
                    continue
                cell, score, rot, sz = match
                state_normalized = (tex_name.lower() in STATE_NORMALIZATIONS)
                matched_textures.append({
                    "face": face_key,
                    "tex_name": tex_name,
                    "file": png_file.name,
                    "cell": cell,
                    "score": score,
                    "rot": rot,
                    "size": sz,
                    "normalized": state_normalized
                })
                
                # Weight score by face priority
                weight = 1.0
                is_active = "Active" in face_key
                is_distinctive_face = any(w in face_key for w in ("Walls", "All", "Up", "Top", "Forward"))
                if is_active: weight *= 1.5
                if is_distinctive_face: weight *= 2.0
                
                # Penalize generic shared plates (e.g. cell (3,1) common bottom, (0,3) common bottom)
                if cell in ((3, 1), (0, 3)):
                    weight *= 0.35
                
                if cell in self.cell_to_blocks:
                    for bid, bname, bcat, bface in self.cell_to_blocks[cell]:
                        bweight = weight
                        if bface in ("walls", "top"): bweight *= 1.5
                        
                        # Category affinity bonus
                        cat_lower = cat.lower()
                        bcat_lower = bcat.lower()
                        if (("machine" in cat_lower and bcat_lower == "crafting") or
                            ("door" in cat_lower and bcat_lower == "door") or
                            ("crate" in cat_lower and bcat_lower == "container") or
                            ("light" in cat_lower and bcat_lower == "pointlight") or
                            ("cannon" in cat_lower and bcat_lower == "gun") or
                            ("thruster" in cat_lower and bcat_lower == "thruster") or
                            ("control" in cat_lower and bcat_lower in ("console", "camera", "gunconsole")) or
                            ("metal" in cat_lower and bcat_lower == "armor") or
                            ("resource" in cat_lower and bcat_lower in ("mineral", "asteroid")) or
                            ("window" in cat_lower and bcat_lower == "window") or
                            ("power" in cat_lower and bcat_lower == "power")):
                            bweight *= 3.0
                            
                        # Semantic ID / Name bonus
                        sbid_clean = sbid.lower().replace("_", "")
                        bname_clean = bname.lower().replace(" ", "").replace("-", "")
                        if any(token in bname_clean for token in sbid_clean.split(":")[-1].split("_") if len(token) > 3):
                            bweight *= 2.0
                            
                        score_by_bid[bid] = score_by_bid.get(bid, 0.0) + score * bweight
                        
            # Determine best matched block ID
            best_bid = None
            best_score = 0.0
            if score_by_bid:
                best_bid, best_score = max(score_by_bid.items(), key=lambda x: x[1])
            else:
                # Heuristic fallback based on category / block ID
                best_bid = self.heuristic_fallback(sbid, cat)
                
            mappings.append({
                "sbid": sbid,
                "sbname": sbname,
                "category": cat,
                "best_bid": best_bid,
                "corneroids_name": self.corneroids_blocks.get(best_bid, {}).get("name", "Unknown"),
                "confidence": best_score,
                "textures": matched_textures
            })
            
        return mappings

    def heuristic_fallback(self, sbid: str, category: str) -> int:
        s = sbid.lower()
        if "air" in s or "void" in s: return 255
        if "pipe" in s or "cable" in s: return 19
        if "beacon" in s: return 19
        if "generator" in s or "power" in s: return 20
        if "storage" in s or "crate" in s or "safe" in s: return 46
        if "window" in s or "frame" in s or "sign" in s: return 47
        if "door" in s: return 28
        if "thruster" in s: return 22
        if "cannon" in s or "gun" in s: return 42
        if "ore" in s or "rock" in s or "mineral" in s: return 0
        return 11 # Default medium steel

def generate_markdown_report(mappings, out_path: Path):
    lines = [
        "# Spacebox to Corneroids Block Texture Mapping Report",
        "",
        "This report is generated by the pixel-by-pixel texture comparison harness.",
        "Each Spacebox block texture was compared against the 16x16 grid of 32x32 cells in `blockTextures.png`.",
        "",
        "| Spacebox Block ID | Spacebox Name | Category | Mapped Corneroids Block | Primary Atlas Cell | Texture Similarity | Notes |",
        "|---|---|---|---|:---:|:---:|---|"
    ]
    
    for m in mappings:
        sbid = m["sbid"]
        sbname = m["sbname"]
        cat = m["category"]
        bid = m["best_bid"]
        cname = m["corneroids_name"]
        
        top_tex = max(m["textures"], key=lambda t: t["score"]) if m["textures"] else None
        cell_str = f"({top_tex['cell'][0]}, {top_tex['cell'][1]})" if top_tex else "-"
        score_str = f"{top_tex['score'] * 100:.1f}%" if top_tex else "-"
        notes = []
        if top_tex and top_tex["score"] >= 0.9999:
            notes.append("Pixel-identical")
        elif top_tex and top_tex["score"] >= 0.95:
            notes.append("High similarity")
        if top_tex and top_tex["normalized"]:
            notes.append("State normalized")
        if not top_tex:
            notes.append("Category fallback")
            
        notes_str = ", ".join(notes)
        lines.append(f"| `{sbid}` | {sbname} | {cat} | **Block {bid}** ({cname}) | `{cell_str}` | {score_str} | {notes_str} |")
        
    out_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"Report written to {out_path}")

if __name__ == "__main__":
    repo_root = Path(__file__).resolve().parents[2]
    default_spacebox = Path.home() / "Downloads" / "Spacebox_v0.1.5.1_windows" / "Mods" / "GameSets" / "Default" / "Blocks"
    parser = argparse.ArgumentParser(description="Spacebox to Corneroids Block Texture Mapping Harness")
    parser.add_argument("--atlas", default=str(repo_root / "byoa" / "Corneroids-Alpha-1.2.0" / "Corneroids" / "Content" / "Textures" / "Blocks" / "blockTextures.png"))
    parser.add_argument("--itemset", default=str(repo_root / "byoa" / "Corneroids-Alpha-1.2.0" / "Corneroids" / "Content" / "Blocksets" / "defaultItemset.xml"))
    parser.add_argument("--spacebox", default=str(default_spacebox))
    parser.add_argument("--report", default=str(repo_root / "docs" / "spacebox-block-mapping.md"))
    parser.add_argument("--json", default=str(repo_root / "docs" / "spacebox-block-mapping.json"))
    
    args = parser.parse_args()
    harness = BlockMappingHarness(Path(args.atlas), Path(args.itemset), Path(args.spacebox))
    mappings = harness.map_all_blocks()
    
    if args.report:
        generate_markdown_report(mappings, Path(args.report))
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(mappings, f, indent=2)
        print(f"JSON mappings written to {args.json}")
