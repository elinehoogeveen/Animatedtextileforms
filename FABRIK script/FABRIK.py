import Rhino.Geometry as rg
import math

# ---------------------------------------------------
# INPUTS
# ---------------------------------------------------

pts = [rg.Point3d(p) for p in points]
lift = y_lift

max_angle = math.radians(min_angle)

# ---------------------------------------------------
# DIST + INITIAL SEGMENT DATA
# ---------------------------------------------------

def dist(a, b):
    return a.DistanceTo(b)

segs = []
orig_dirs = []

for i in range(len(pts) - 1):
    seg_len = dist(pts[i], pts[i + 1])
    segs.append(seg_len)

    d = pts[i + 1] - pts[i]
    if not d.IsTiny():
        d.Unitize()
    orig_dirs.append(d)

# ---------------------------------------------------
# CONE CLAMP
# ---------------------------------------------------

def clamp_vector_to_cone(vec, axis, max_angle):

    v = rg.Vector3d(vec)
    a = rg.Vector3d(axis)

    if v.IsTiny():
        return a

    v.Unitize()
    a.Unitize()

    angle = rg.Vector3d.VectorAngle(a, v)

    # already inside cone
    if angle <= max_angle:
        return v

    rot_axis = rg.Vector3d.CrossProduct(a, v)

    if rot_axis.IsTiny():
        return a

    rot_axis.Unitize()

    constrained = rg.Vector3d(a)

    rot = rg.Transform.Rotation(
        max_angle,
        rot_axis,
        rg.Point3d.Origin
    )

    constrained.Transform(rot)
    constrained.Unitize()

    return constrained

# ---------------------------------------------------
# END PIN
# ---------------------------------------------------

end = rg.Point3d(pts[-1])

# ---------------------------------------------------
# FABRIK LOOP
# ---------------------------------------------------

for _ in range(200):

    # ------------------------------------------------
    # HARD CONSTRAINTS (IMPORTANT)
    # ------------------------------------------------

    pts[0] = rg.Point3d(pts[0].X, lift, pts[0].Z)
    pts[-1] = end

    # =================================================
    # FORWARD PASS
    # =================================================

    for i in range(1, len(pts) - 1):  # ❗ FIXED: exclude last point

        parent = pts[i - 1]
        current = pts[i]

        vec = current - parent

        if vec.IsTiny():
            continue

        vec.Unitize()

        # dynamic stability: blend original + current direction
        axis = rg.Vector3d(orig_dirs[i - 1])

        vec = clamp_vector_to_cone(vec, axis, max_angle)

        pts[i] = parent + vec * segs[i - 1]

        # floor constraint
        if pts[i].Y < 0:
            pts[i] = rg.Point3d(pts[i].X, 0, pts[i].Z)

    # =================================================
    # BACKWARD PASS
    # =================================================

    pts[-1] = end  # re-pin before solving backward

    for i in range(len(pts) - 2, 0, -1):

        child = pts[i + 1]
        current = pts[i]

        vec = current - child

        if vec.IsTiny():
            continue

        vec.Unitize()

        axis = -rg.Vector3d(orig_dirs[i])

        vec = clamp_vector_to_cone(vec, axis, max_angle)

        pts[i] = child + vec * segs[i]

        # floor constraint
        if pts[i].Y < 0:
            pts[i] = rg.Point3d(pts[i].X, 0, pts[i].Z)

    # final reinforcement (prevents drift accumulation)
    pts[-1] = end

# ---------------------------------------------------
# OUTPUT
# ---------------------------------------------------

a = pts