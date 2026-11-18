import sys
from pathlib import Path
import numpy as np
import trimesh
from pxr import Usd, UsdGeom


def usd_to_obj(usd_path: str) -> None:
    src = Path(usd_path).resolve()
    if not src.exists():
        raise FileNotFoundError(f"File not found: {src}")

    stage = Usd.Stage.Open(str(src))
    scene = trimesh.Scene()
    xform_cache = UsdGeom.XformCache(Usd.TimeCode.Default())

    for prim in stage.Traverse():
        if not prim.IsA(UsdGeom.Mesh):
            continue

        usd_mesh = UsdGeom.Mesh(prim)
        points_attr = usd_mesh.GetPointsAttr().Get()
        counts_attr = usd_mesh.GetFaceVertexCountsAttr().Get()
        indices_attr = usd_mesh.GetFaceVertexIndicesAttr().Get()

        if not points_attr or not counts_attr or not indices_attr:
            continue

        # Transform local vertices to world space so parts stay assembled
        world_mat = np.array(
            xform_cache.GetLocalToWorldTransform(prim), dtype=np.float64
        )
        pts = np.array(points_attr, dtype=np.float64)
        pts_h = np.hstack([pts, np.ones((len(pts), 1))])
        world_pts = (pts_h @ world_mat)[:, :3]

        # Fan-triangulate quads/n-gons for Unity compatibility
        faces = []
        idx = 0
        for count in counts_attr:
            poly = indices_attr[idx : idx + count]
            idx += count
            for i in range(1, count - 1):
                faces.append([poly[0], poly[i], poly[i + 1]])

        if not faces:
            continue

        mesh = trimesh.Trimesh(
            vertices=world_pts, faces=np.array(faces), process=False
        )
        part_name = prim.GetPath().pathString.strip("/").replace("/", "_")
        scene.add_geometry(mesh, node_name=part_name, geom_name=part_name)

    out_path = src.with_suffix(".obj")
    scene.export(str(out_path))
    print(f"Exported {len(scene.geometry)} meshes to {out_path}")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: uv run convert.py <path_to_model.usd>")
        sys.exit(1)
    usd_to_obj(sys.argv[1])
