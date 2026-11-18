import os
import sys
import zipfile
import tempfile
import shutil
import trimesh

SUPPORTED_EXTS = (".step", ".stp", ".iges", ".igs", ".stl", ".glb", ".gltf")

def convert_to_obj(cad_path, out_obj_path, max_faces=45000):
    loaded = trimesh.load(cad_path)
    if isinstance(loaded, trimesh.Scene):
        if not loaded.geometry:
            raise ValueError("Scene contains no geometry.")
        mesh = trimesh.util.concatenate(tuple(loaded.geometry.values()))
    else:
        mesh = loaded

    if not hasattr(mesh, "faces") or len(mesh.faces) == 0:
        raise ValueError("No valid triangle faces extracted.")

    orig_faces = len(mesh.faces)
    note = ""
    if orig_faces > max_faces:
        mesh = mesh.simplify_quadric_decimation(face_count=max_faces)
        note = f" (decimated {orig_faces:,} -> {len(mesh.faces):,} faces)"

    mesh.export(out_obj_path)
    size_mb = os.path.getsize(out_obj_path) / (1024 * 1024)
    return size_mb, note

def batch_process(input_path, output_dir, prefix="Rob_"):
    os.makedirs(output_dir, exist_ok=True)
    temp_dir = tempfile.mkdtemp(prefix="forge_cad_")
    targets = []

    try:
        if os.path.isdir(input_path):
            for root, _, files in os.walk(input_path):
                for fname in files:
                    fpath = os.path.join(root, fname)
                    if fname.lower().endswith(".zip"):
                        with zipfile.ZipFile(fpath, "r") as zf:
                            zf.extractall(os.path.join(temp_dir, os.path.splitext(fname)[0]))
                    elif fname.lower().endswith(SUPPORTED_EXTS):
                        targets.append(fpath)
        elif input_path.lower().endswith(".zip"):
            with zipfile.ZipFile(input_path, "r") as zf:
                zf.extractall(temp_dir)
        elif input_path.lower().endswith(SUPPORTED_EXTS):
            targets.append(input_path)

        for root, _, files in os.walk(temp_dir):
            for fname in files:
                if fname.startswith("~$"):
                    continue
                lower = fname.lower()
                if lower.endswith((".obj", ".mtl")):
                    out_direct = os.path.join(output_dir, f"{prefix}{fname.replace(' ', '_')}")
                    shutil.copy2(os.path.join(root, fname), out_direct)
                    print(f"[COPIED] {fname} -> {os.path.basename(out_direct)}")
                elif lower.endswith(SUPPORTED_EXTS):
                    targets.append(os.path.join(root, fname))

        if not targets:
            print("No additional .step/.stp/.stl/.glb files needed conversion.")
            return

        for idx, src in enumerate(sorted(targets), 1):
            clean_name = os.path.splitext(os.path.basename(src))[0].replace(" ", "_")
            out_name = f"{prefix}{clean_name}.obj" if not clean_name.startswith(prefix) else f"{clean_name}.obj"
            out_path = os.path.join(output_dir, out_name)
            try:
                print(f"[{idx}/{len(targets)}] Converting {os.path.basename(src)}...")
                size_mb, note = convert_to_obj(src, out_path)
                print(f"   -> {out_name} ({size_mb:.2f} MB){note}")
            except Exception as e:
                print(f"   [ERROR] {os.path.basename(src)}: {e}")
    finally:
        shutil.rmtree(temp_dir, ignore_errors=True)

if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: uv run python batch_cad_to_obj.py <zip_or_step_or_folder> [output_dir] [prefix]")
        sys.exit(1)
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else "unity_project/Assets/Models/W3_Robotics"
    pfx = sys.argv[3] if len(sys.argv) > 3 else "Rob_"
    batch_process(src, dst, pfx)
