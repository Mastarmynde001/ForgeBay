import array
import os
import shutil

SRC_OBJ = "Assets/Models/W1_Mechanical/V8 ENGINE/Product6.obj"
SRC_MTL = "Assets/Models/W1_Mechanical/V8 ENGINE/Product6.mtl"
DST_DIR = "Assets/Models/W1_Mechanical"
DST_OBJ = os.path.join(DST_DIR, "V8_Engine_Optimized.obj")
DST_MTL = os.path.join(DST_DIR, "V8_Engine_Optimized.mtl")
GRID_DIVISIONS = 220  # 220 voxels along longest axis (~10-25 MB output)

def main():
    print("Pass 1/3: Scanning bounding box (streaming, low RAM)...")
    min_x = min_y = min_z = float("inf")
    max_x = max_y = max_z = float("-inf")
    v_count = 0

    with open(SRC_OBJ, "r", encoding="utf-8", errors="ignore") as f:
        for line in f:
            if line.startswith("v "):
                parts = line.split()
                x, y, z = float(parts[1]), float(parts[2]), float(parts[3])
                if x < min_x: min_x = x
                if y < min_y: min_y = y
                if z < min_z: min_z = z
                if x > max_x: max_x = x
                if y > max_y: max_y = y
                if z > max_z: max_z = z
                v_count += 1

    span = max(max_x - min_x, max_y - min_y, max_z - min_z, 1e-6)
    inv_cell = GRID_DIVISIONS / span
    print(f"Scanned {v_count:,} vertices. Span={span:.4f}")

    print("Pass 2/3: Clustering vertices into 3D grid...")
    v_to_new = array.array("I", [0]) * (v_count + 1)
    cell_to_new = {}
    sum_x, sum_y, sum_z, counts = [], [], [], []
    next_id = 1
    v_idx = 0

    with open(SRC_OBJ, "r", encoding="utf-8", errors="ignore") as f:
        for line in f:
            if line.startswith("v "):
                v_idx += 1
                parts = line.split()
                x, y, z = float(parts[1]), float(parts[2]), float(parts[3])
                ix = int((x - min_x) * inv_cell)
                iy = int((y - min_y) * inv_cell)
                iz = int((z - min_z) * inv_cell)
                key = (ix << 40) | (iy << 20) | iz
                nid = cell_to_new.get(key)
                if nid is None:
                    nid = next_id
                    next_id += 1
                    cell_to_new[key] = nid
                    sum_x.append(x)
                    sum_y.append(y)
                    sum_z.append(z)
                    counts.append(1)
                else:
                    idx0 = nid - 1
                    sum_x[idx0] += x
                    sum_y[idx0] += y
                    sum_z[idx0] += z
                    counts[idx0] += 1
                v_to_new[v_idx] = nid

    cell_to_new.clear()
    print(f"Compressed {v_count:,} -> {next_id - 1:,} vertices.")

    print("Pass 3/3: Writing optimized OBJ with intact sub-parts & materials...")
    if os.path.exists(SRC_MTL):
        shutil.copy2(SRC_MTL, DST_MTL)

    seen_faces = set()
    cur_v = 0
    kept_faces = 0

    with open(SRC_OBJ, "r", encoding="utf-8", errors="ignore") as fin, \
         open(DST_OBJ, "w", encoding="utf-8") as fout:
        if os.path.exists(DST_MTL):
            fout.write("mtllib V8_Engine_Optimized.mtl\n")

        for i in range(next_id - 1):
            c = counts[i]
            fout.write(f"v {sum_x[i]/c:.6f} {sum_y[i]/c:.6f} {sum_z[i]/c:.6f}\n")

        for line in fin:
            if line.startswith("v "):
                cur_v += 1
            elif line.startswith(("o ", "g ")):
                seen_faces.clear()
                fout.write(line)
            elif line.startswith("usemtl "):
                fout.write(line)
            elif line.startswith("f "):
                tokens = line.split()[1:]
                if len(tokens) < 3:
                    continue
                remapped = []
                for tok in tokens:
                    raw_vi = int(tok.split("/")[0])
                    vi = (cur_v + raw_vi + 1) if raw_vi < 0 else raw_vi
                    remapped.append(v_to_new[vi])

                for k in range(1, len(remapped) - 1):
                    a, b, c_v = remapped[0], remapped[k], remapped[k + 1]
                    if a == b or b == c_v or a == c_v:
                        continue
                    tri_key = tuple(sorted((a, b, c_v)))
                    if tri_key not in seen_faces:
                        seen_faces.add(tri_key)
                        fout.write(f"f {a} {b} {c_v}\n")
                        kept_faces += 1

    size_mb = os.path.getsize(DST_OBJ) / (1024 * 1024)
    print(f"Success! Created {DST_OBJ} ({size_mb:.2f} MB, {kept_faces:,} triangles).")

if __name__ == "__main__":
    main()
