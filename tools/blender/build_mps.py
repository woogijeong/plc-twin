"""
build_mps.py - rebuilds the PlcTwin MPS line in Blender with more modelling detail (own work, script-generated).

Run (headless, Blender 5.1):
  blender.exe -b --factory-startup -P tools/blender/build_mps.py [-- --engine EEVEE|WORKBENCH] [--no-render]
Writes tools/blender/out/mps.glb, mps.fbx, mps.blend and the previews in tools/out/blender_preview/.

SOURCE OF TRUTH
  Every pivot, anchor and dimension below is copied from PlcTwin/Assets/PlcTwin/Sim/MpsGeometry.cs and
  Editor/SceneBuilder.cs (Line, Magazine, CylinderUnit, Drill, Conveyor, OpenBox, Lift, Stocker). Extra detail is only
  added inside the original part's bounding box or as new, clearly named static pieces (CableDuct, MagazineBracket,
  StopperArm, ShelfBracket); check() asserts the anchors the Sim relies on.

UNITS AND AXES
  1 Blender unit = 1 m = 1 Unity unit.
  MPS-local (SceneBuilder, left-handed like Unity): +z up, +y toward the conveyor end (camera side), +x right/back.
  Unity world  = (x, z, -y)   (SceneBuilder: MPS root rotation Euler(-90, 0, 0))
  Blender world = (-x, y, z)  (right-handed, Z up; the x flip is the handedness change, not a visual mirror)
  => Blender (bx, by, bz) -> Unity (-bx, bz, -by). That is exactly what Unity's FBX importer and glTFast do with a
     Blender export using Forward -Z / Up +Y, so meshes land on the current scene 1:1.
  Every object has identity rotation and unit scale; its origin is the SceneBuilder pivot (rest position) and the
  lamp/switch module tilt is baked into the mesh. The empty/mesh hierarchy mirrors the SceneBuilder nodes.
  Unity hookup (later step): MpsView moves movers along their parent's MPS-local axes, so keep SceneBuilder's node
  skeleton under "MPS" and attach each imported mesh as a child of the node with the same name, localPosition 0,
  localRotation Euler(90, 0, 0) (undoes the MPS root rotation for Unity-frame mesh data).

NAMES AND MATERIALS
  Blender forces unique object names ("Base", "Base.001", ...). Every object carries a custom property unity_name
  with the SceneBuilder name (exported as glTF extras / FBX user properties). The 48 names MpsView binds are unique
  and exact (asserted). Material slot names are the SceneBuilder material keys (White, CylinderBody, LightBlue, Yellow,
  Orange, Purple, Belt, Cream, Nut, LampOff, SwitchOff, ChipMetal, ChipNonmetal) so TwinStyle can keep swapping them;
  parts under a mover use "M_<key>" (style-A orange/teal accents). Lamps carry only "LampOff", switches only
  "SwitchOff", so Unity can swap on/off materials per renderer.

EXPORT
  out/mps.glb  glTF binary, +Y up, extras on.
  out/mps.fbx  Forward -Z, Up Y, Apply Scalings = FBX Units Scale, Apply Transform (bake space transform) on,
               custom properties on, empties + meshes only.
"""
import bpy, bmesh, math, os, sys, time
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT_MODEL = os.path.join(HERE, "out")
OUT_PREV = os.path.join(ROOT, "tools", "out", "blender_preview")
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
ENGINE = ARGS[ARGS.index("--engine") + 1] if "--engine" in ARGS else "EEVEE"

# ---------------------------------------------------------------- MpsGeometry.cs (micrometres)
SupplyRootX, SupplyRootY, SupplyRet, SupplyExt = -195000, -377000, 15000, 100000
BeltX = 35000
EmitRootX, EmitRootY, EmitRet = BeltX, -537000, 15000
DrillRootX, DrillRootY, DrillUp, DrillDown = 55000, -399000, 80000, 10000
DischargeRootX, DischargeRootY, DischargeRet = 180000, -6000, -15000
StopperUp, StopperDown = 65000, 40000
LiftRootX, LiftRootY, LiftRet = 172000, 344000, -30000
StockerRootX, StockerRootY, StockerRet, StockerExt = -85000, 414000, -55000, -105000
ChipR, ChipHalfT = 20000, 5000
StageX, StageY = BeltX, -401500
MagX, MagY = StageX - (SupplyExt - SupplyRet), StageY
BeltMinY = 75000 - 440000
SorterY = 75000 - 211000
StopperY, StopperHalfY = 75000 + 209000, 30000
StopY = StopperY - (StopperHalfY + ChipR)
Box1X, Box1Y, Box2X, Box2Y = 35000, 560000, -45000, -31000
BeltChipZ = -83000
TableChipZ = BeltChipZ
ServoStartUm = 90000
PadDx, PadDy, PadDz = -107000, StopY - LiftRootY, -28000
SlotDx, SlotDy, StockerZ = 50000, LiftRootY + PadDy - (StockerRootY + StockerExt), -38000
ShelfZ = [90000, 35000, -20000]
BaseTop = -0.1585


def F(um):
    return um * 1e-6


X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
UP, FRONT, LEFT = Z, Y, -X  # SceneBuilder Up / Front / Left (MPS-local +z, +y, -x)

# ---------------------------------------------------------------- materials: style palette A (SceneBuilder.StylePalette)
PAL = {
    "White": (208, 211, 216), "CylinderBody": (182, 187, 194), "LightBlue": (196, 200, 206), "Yellow": (160, 165, 172),
    "Orange": (160, 165, 172), "Purple": (140, 145, 152), "Belt": (62, 66, 72), "Cream": (226, 226, 220),
    "Nut": (120, 125, 132),
    "M_White": (0, 150, 145), "M_CylinderBody": (0, 150, 145), "M_Nut": (0, 120, 116), "M_LightBlue": (255, 120, 20),
    "M_Yellow": (255, 120, 20), "M_Orange": (255, 120, 20),
    "LampOff": (245, 245, 245), "SwitchOff": (255, 0, 0), "ChipMetal": (0, 20, 255), "ChipNonmetal": (204, 0, 0),
}
MAT = {}


def lin(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def mat(key):
    if key not in MAT:
        m = bpy.data.materials.new(key)
        rgba = tuple(lin(c) for c in PAL[key]) + (1.0,)
        m.diffuse_color = rgba  # Workbench / viewport
        bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        bsdf.inputs["Base Color"].default_value = rgba
        bsdf.inputs["Roughness"].default_value = 0.85 if key.startswith("M_") else 0.5 if key in ("LampOff", "SwitchOff") else 0.9
        MAT[key] = m
    return MAT[key]


# ---------------------------------------------------------------- scene graph helpers
COL = None
AX = {"x": Matrix.Rotation(math.pi / 2, 4, "Y"), "y": Matrix.Rotation(-math.pi / 2, 4, "X"), "z": Matrix.Identity(4)}


LAYOUT = []  # b blender name, u unity name, p parent blender name, l MPS-local position, m has mesh -> out/layout.json


def place(name, parent, local, data):
    o = bpy.data.objects.new(name, data)
    o["unity_name"] = name
    LAYOUT.append({"b": o.name, "u": name, "p": parent.obj.name if parent else "", "l": list(local), "m": data is not None})
    COL.objects.link(o)
    if parent:
        o.parent = parent.obj  # parent inverse stays identity: location is the local position
    o.location = (-local[0], local[1], local[2])  # MPS -> Blender
    return o


class Node:
    """Empty = SceneBuilder Node (pivot only)."""
    def __init__(self, name, parent, local, mover=None):
        self.mover = (parent.mover if parent else False) if mover is None else mover
        self.obj = place(name, parent, local, None)
        self.obj.empty_display_size = 0.01


class Part:
    """One mesh object. Shapes are given in MPS-local metres relative to the object origin."""
    def __init__(self, name, parent, local=(0, 0, 0), rot=None, mover=None):
        self.name, self.parent, self.local, self.rot = name, parent, tuple(local), rot
        self.mover = (parent.mover if parent else False) if mover is None else mover
        self.bm, self.mats = bmesh.new(), []

    def add(self, t, key):
        if self.mover and ("M_" + key) in PAL:
            key = "M_" + key
        if key not in self.mats:
            self.mats.append(key)
        bmesh.ops.recalc_face_normals(t, faces=t.faces[:])
        for f in t.faces:
            f.material_index = self.mats.index(key)
        me = bpy.data.meshes.new("_tmp")
        t.to_mesh(me)
        t.free()
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        return self

    def box(self, c, size, key, bev=0.0012, segs=1):
        t = bmesh.new()
        bmesh.ops.create_cube(t, size=1.0)
        bmesh.ops.scale(t, vec=Vector(size), verts=t.verts)
        b = min(bev, 0.45 * min(size))
        if b > 2e-5:
            bmesh.ops.bevel(t, geom=t.edges[:], offset=b, segments=segs, affect="EDGES", clamp_overlap=True)
        bmesh.ops.translate(t, vec=Vector(c), verts=t.verts)
        return self.add(t, key)

    def cyl(self, c, axis, d, length, key, segs=24, bev=0.0, d2=None):
        """Cylinder (or cone: d bottom, d2 top) of diameter d along MPS-local axis; segs < 12 = flat (hex heads)."""
        t = bmesh.new()
        bmesh.ops.create_cone(t, cap_ends=True, cap_tris=False, segments=segs, radius1=d / 2,
                              radius2=(d if d2 is None else d2) / 2, depth=length)
        if bev > 0:
            rim = [e for e in t.edges if any(len(f.verts) > 4 for f in e.link_faces)]
            bmesh.ops.bevel(t, geom=rim, offset=min(bev, 0.4 * length, 0.2 * min(d, d2 or d)), segments=1,
                            affect="EDGES", clamp_overlap=True)
        for f in t.faces:
            f.smooth = segs >= 12 and len(f.verts) == 4
        bmesh.ops.transform(t, matrix=AX[axis], verts=t.verts)
        bmesh.ops.translate(t, vec=Vector(c), verts=t.verts)
        return self.add(t, key)

    def ring(self, outer, inner, axis, c, length, key, closed=True):
        """Extrude the band between two point loops (same count) along axis; side faces smooth, caps flat."""
        t, n = bmesh.new(), len(outer)
        P = {"x": lambda u, v, w: (w, u, v), "y": lambda u, v, w: (u, w, v), "z": lambda u, v, w: (u, v, w)}[axis]
        O0 = [t.verts.new(P(u, v, -length / 2)) for u, v in outer]
        O1 = [t.verts.new(P(u, v, length / 2)) for u, v in outer]
        I0 = [t.verts.new(P(u, v, -length / 2)) for u, v in inner]
        I1 = [t.verts.new(P(u, v, length / 2)) for u, v in inner]
        for i in range(n if closed else n - 1):
            j = (i + 1) % n
            t.faces.new((O0[i], O0[j], O1[j], O1[i])).smooth = True
            t.faces.new((I0[j], I0[i], I1[i], I1[j])).smooth = True
            t.faces.new((O0[j], O0[i], I0[i], I0[j]))
            t.faces.new((O1[i], O1[j], I1[j], I1[i]))
        if not closed:
            for k in (0, n - 1):
                t.faces.new((O0[k], I0[k], I1[k], O1[k]))
        bmesh.ops.translate(t, vec=Vector(c), verts=t.verts)
        return self.add(t, key)

    def finish(self):
        bm = self.bm
        if self.rot:
            bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=self.rot.to_matrix())
        bmesh.ops.scale(bm, vec=(-1, 1, 1), verts=bm.verts)  # MPS -> Blender
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        for k in self.mats:
            me.materials.append(mat(k))
        self.obj = place(self.name, self.parent, self.local, me)
        return self


def slotted(p, c, size, key, axis, n=1, slot="Belt", w=None, bev=0.0015):
    """Aluminium T-slot profile look: bevelled box + n dark slot strips on each face parallel to axis."""
    p.box(c, size, key, bev)
    ai = "xyz".index(axis)
    for fi in range(3):
        if fi == ai:
            continue
        oi = 3 - ai - fi
        sw = w or min(0.006, 0.22 * size[oi] / n)
        for sgn in (-1, 1):
            for k in range(n):
                cc, ss = list(c), [0.0, 0.0, 0.0]
                cc[fi] += sgn * size[fi] / 2
                cc[oi] += (k - (n - 1) / 2) * size[oi] / n
                ss[ai], ss[fi], ss[oi] = size[ai] - 4 * bev, 0.0008, sw
                p.box(cc, ss, slot, 0)


def frame_block(p, c, size, key, panel, post=0.01, plate=0.006):
    """Welded-frame look inside the same bbox: top/bottom plates, corner posts, recessed side panels."""
    sx, sy, sz = size
    cx, cy, cz = c
    p.box((cx, cy, cz + sz / 2 - plate / 2), (sx, sy, plate), key, 0.0012)
    p.box((cx, cy, cz - sz / 2 + plate / 2), (sx, sy, plate), key, 0.0012)
    for i in (-1, 1):
        for j in (-1, 1):
            p.box((cx + i * (sx / 2 - post / 2), cy + j * (sy / 2 - post / 2), cz), (post, post, sz - 2 * plate + 0.001), key, 0.0008)
    p.box((cx, cy, cz), (sx - 0.002, sy - 0.002, sz - 2 * plate), panel, 0)


def hexbolt(p, c, axis, d=0.005, h=0.0016):
    return p.cyl(c, axis, d, h, "Nut", segs=6)


# ---------------------------------------------------------------- lamps and switches (ref §4)
def module(parent, prefix, name, pos, normal):
    q = Vector((0, 0, 1)).rotation_difference(Vector(normal))  # = Unity Quaternion.FromToRotation(forward, normal)
    m = Node(prefix + name, parent, pos, mover=False)
    b = Part("Base", m, (0, 0, 0), rot=q, mover=False)
    b.box((0, 0, 0.0025), (0.02, 0.02, 0.005), "White", 0.0008)
    b.cyl((0, 0, 0.0054), "z", 0.018, 0.0008, "Nut", segs=24)  # dark bezel ring
    b.finish()
    return m, q


def Lamp(parent, name, pos, normal):
    m, q = module(parent, "LampModule", name, pos, normal)
    lp = Part("Lamp" + name, m, q @ Vector((0, 0, 0.0065)), rot=q, mover=False)
    lp.cyl((0, 0, -0.0005), "z", 0.015, 0.002, "LampOff", segs=24, bev=0.0003)
    lp.cyl((0, 0, 0.001), "z", 0.013, 0.001, "LampOff", segs=24, d2=0.009)  # domed lens
    lp.finish()


def Switch(parent, name, pos, normal):
    m, q = module(parent, "SwitchModule", name, pos, normal)
    sw = Part("Switch" + name, m, q @ Vector((0, 0, 0.0085)), rot=q, mover=False)
    sw.cyl((0, 0, -0.0005), "z", 0.015, 0.006, "SwitchOff", segs=24, bev=0.0004)
    sw.cyl((0, 0, 0.003), "z", 0.015, 0.001, "SwitchOff", segs=24, d2=0.0125)  # chamfered cap
    sw.finish()


# ---------------------------------------------------------------- the line (SceneBuilder.Line)
def build():
    mps = Node("MPS", None, (0, 0, 0))
    b = Node("Base", mps, (0, 0, 0))
    p = Part("Base:1", b, (0, 0, -0.1635))
    p.box((0, 0, 0), (0.50, 1.20, 0.01), "White", 0.002)
    for i in range(-2, 3):
        p.box((i * 0.1, 0, 0.005), (0.004, 1.17, 0.0008), "Belt", 0)  # T-slot grooves
    for i in (-1, 1):
        for j in (-1, 1):
            p.cyl((i * 0.22, j * 0.57, -0.009), "z", 0.03, 0.008, "Belt", segs=20)  # rubber feet
    p.finish()
    duct = Part("CableDuct", b, (-0.235, 0.135, BaseTop))  # added: slotted cable duct along the free -x edge
    L = 0.86
    duct.box((0, 0, 0.009), (0.02, L, 0.018), "Purple", 0.001)
    duct.box((0, 0, 0.0185), (0.022, L + 0.002, 0.002), "Belt", 0.0006)
    for k in range(43):
        for s in (-1, 1):
            duct.box((s * 0.01, -L / 2 + 0.01 + k * 0.02, 0.008), (0.0008, 0.004, 0.012), "Belt", 0)
    duct.finish()

    tableTop = F(TableChipZ - ChipHalfT)
    tb, tf = -0.432, F(BeltMinY)
    table = Node("WorkTable", mps, (F(StageX) - 0.055, (tb + tf) / 2, 0))
    wt = Part("WorkTableBlock", table, (0, 0, (BaseTop + tableTop) / 2))
    frame_block(wt, (0, 0, 0), (0.14, tf - tb, tableTop - BaseTop), "White", "CylinderBody")
    wt.finish()
    Lamp(table, "SupplySensor", (-0.03, (tf - tb) / 2, (BaseTop + tableTop) / 2), FRONT)

    magazine(mps, tableTop)

    cylinder_unit(mps, "CylinderSupply", "CylinerSupplyInner", "SupplyGripper", "Supply", (F(SupplyRootX), F(SupplyRootY), F(TableChipZ)),
                  0, +1, SupplyRet, F(MagX - ChipR - SupplyRootX - SupplyRet), F(MagY - SupplyRootY))
    cylinder_unit(mps, "CylinderEmit", "CylinderEmitInner", "EmitGripper", "Emit", (F(EmitRootX), F(EmitRootY), F(TableChipZ)),
                  1, +1, EmitRet, F(StageY - ChipR - EmitRootY - EmitRet), F(StageX - EmitRootX))
    cylinder_unit(mps, "CylinderDischarge", "CylinderDischargeInner", "DischargeGripper", "Discharge",
                  (F(DischargeRootX), F(DischargeRootY), F(BeltChipZ)), 0, -1, DischargeRet, F(BeltX + ChipR - DischargeRootX - DischargeRet), 0)
    drill(mps, tableTop)
    conveyor(mps)
    open_box(mps, "Box2", (F(Box2X), F(Box2Y), BaseTop), (0.08, 0.128, 0.064))
    open_box(mps, "Box1", (F(Box1X), F(Box1Y), BaseTop), (0.08, 0.08, 0.064))
    lift(mps)
    stocker(mps)


def magazine(mps, tableTop):
    """Yellow half-pipe ø42 x 100 open toward +y; bottom 12 mm free for the supply gripper."""
    t = Node("Magazine", mps, (F(MagX), F(MagY), tableTop))
    n = 17
    arc = [math.pi * i / (n - 1) for i in range(n)]
    pts = lambda r: [(r * math.cos(a), -r * math.sin(a)) for a in arc]
    s = Part("Sorter:1", t)  # the 7 box segments Sorter:1..7 become one half-pipe mesh
    s.ring(pts(0.0235), pts(0.0205), "z", (0, 0, 0.056), 0.088, "Yellow", closed=False)
    s.ring(pts(0.027), pts(0.0205), "z", (0, 0, 0.098), 0.004, "Yellow", closed=False)  # top flange
    s.finish()
    br = Part("MagazineBracket", t)  # added: holds the pipe from behind, clear of the gripper path (|y| < 0.03, z < 0.02)
    br.box((0, -0.025, 0.07), (0.02, 0.003, 0.05), "Nut", 0.0006)
    br.box((0, -0.0305, 0.085), (0.012, 0.009, 0.01), "Nut", 0.0008)
    zb = BaseTop - tableTop
    br.box((0, -0.0365, (zb + 0.09) / 2), (0.012, 0.006, 0.09 - zb), "Nut", 0.0008)
    br.box((0, -0.042, zb + 0.002), (0.03, 0.02, 0.004), "Nut", 0.0008)
    br.finish()


def cylinder_unit(mps, name, inner, gripper, sw, root, axis, d, ret_um, g, across):
    u = Node(name, mps, root)
    a, q = (X, Y) if axis == 0 else (Y, X)
    ac = "x" if axis == 0 else "y"
    S = lambda al, acr, z: tuple(a * al + q * acr + Z * z)
    bottom, top = BaseTop - root[2], -0.012
    ped = Part("CylinderBaseNormal", u, S(0, 0, (bottom + top) / 2))
    frame_block(ped, (0, 0, 0), tuple(a * 0.14 + q * 0.10 + Z * (top - bottom)), "CylinderBody", "Purple")
    ped.finish()
    bar = Part("CylinderShaft", u)
    bar.cyl((0, 0, 0), ac, 0.02, 0.15, "White", segs=32)
    for i in (-1, 1):
        for j in (-1, 1):
            bar.cyl(tuple(q * i * 0.0115 + Z * j * 0.0115), ac, 0.0022, 0.132, "Nut", segs=8)  # tie rods
    bar.finish()
    for k, sgn in ((1, -1), (2, 1)):
        nut = Part("Nut:%d" % k, u, S(sgn * 0.07, 0, 0))  # round nuts become square end caps
        nut.box((0, 0, 0), tuple(a * 0.008 + q * 0.03 + Z * 0.03), "Nut", 0.002)
        nut.cyl((0, 0, 0.0165), "z", 0.006, 0.004, "Belt", segs=12)  # air port fitting
        for i in (-1, 1):
            for j in (-1, 1):
                hexbolt(nut, S(sgn * 0.0045, i * 0.0115, j * 0.0115), ac, 0.004, 0.002)
        nut.finish()
    t = Node(inner, u, tuple(a * F(ret_um)), mover=True)
    rod = Part("CylinderInnerShaft", t, S(g - d * 0.10, 0, 0))
    rod.cyl((0, 0, 0), ac, 0.01, 0.16, "White", segs=24)
    if gripper:
        rod.cyl(S(d * 0.075, 0, 0), ac, 0.014, 0.006, "Nut", segs=6)  # rod-end lock nut
    rod.finish()
    if gripper:
        gp = Part(gripper, t, S(g - d * 0.01, across, 0))
        gp.box(S(-d * 0.002, 0, 0), tuple(a * 0.016 + q * 0.06 + Z * 0.03), "LightBlue", 0.002)
        gp.box(S(d * 0.008, 0, 0), tuple(a * 0.004 + q * 0.052 + Z * 0.024), "Belt", 0.001)  # rubber face, flush with the push face
        for i in (-1, 1):
            hexbolt(gp, S(-d * 0.0105, i * 0.02, 0), ac, 0.006, 0.002)
        gp.finish()
        if abs(across) > 0.03:
            Part(gripper + "Arm", t, S(g - d * 0.01, across / 2, 0)).box((0, 0, 0), tuple(a * 0.01 + q * abs(across) + Z * 0.01), "LightBlue", 0.001).finish()
    cam = 0.035 if axis == 0 else -0.035
    Switch(u, sw + "Forward", S(d * 0.055, cam, top), UP)
    Lamp(u, sw + "Forward", S(d * 0.055, -cam, top), UP)
    Switch(u, sw + "Backward", S(-d * 0.055, cam, top), UP)
    Lamp(u, sw + "Backward", S(-d * 0.055, -cam, top), UP)
    return t


def drill(mps, tableTop):
    rootZ = tableTop + 0.010 + 0.035 - F(DrillDown)
    u = Node("Drill", mps, (F(DrillRootX), F(DrillRootY), rootZ))
    col = Part("DrillStage", u, (0.035, 0, (BaseTop + 0.0415) / 2 - rootZ))
    slotted(col, (0, 0, -0.0015), (0.04, 0.06, 0.197), "LightBlue", "z")
    col.box((0, 0, 0.0985), (0.042, 0.062, 0.003), "Nut", 0.0008)  # top cap
    col.box((-0.0215, 0, 0.01), (0.003, 0.012, 0.17), "Nut", 0.0005)  # linear guide rail
    col.finish()
    unit = Node("DrillUnit", u, (0, 0, F(DrillUp)), mover=True)
    sx = F(StageX - DrillRootX)
    db = Part("DrillBase", unit, (sx + 0.0025, 0, 0))
    db.box((0, 0, 0), (0.075, 0.05, 0.008), "Yellow", 0.0015)
    for i in (-1, 1):
        for j in (-1, 1):
            hexbolt(db, (i * 0.03, j * 0.018, 0.0048), "z")
    db.finish()
    mc = Part("DrillCover", unit, (sx, 0, 0.0165))  # cube cover becomes a ribbed motor
    mc.cyl((0, 0, -0.0015), "z", 0.024, 0.022, "Orange", segs=32, bev=0.001)
    for zz in (-0.007, -0.001, 0.005):
        mc.cyl((0, 0, zz), "z", 0.026, 0.0015, "Orange", segs=32)
    mc.cyl((0, 0, 0.011), "z", 0.016, 0.003, "Nut", segs=24, bev=0.0005)
    mc.box((0, 0.0135, -0.002), (0.008, 0.004, 0.008), "Nut", 0.0008)  # terminal box
    mc.finish()
    bit = Part("Drill:1", unit, (sx, 0, -0.019))  # tip stays at -0.034 below the unit
    bit.cyl((0, 0, 0.011), "z", 0.01, 0.008, "Nut", segs=20, bev=0.0006)  # chuck
    bit.cyl((0, 0, 0.005), "z", 0.006, 0.004, "Nut", segs=20, d2=0.01)  # chuck nose
    bit.cyl((0, 0, -0.0045), "z", 0.004, 0.015, "White", segs=16)
    bit.cyl((0, 0, -0.0135), "z", 0.0004, 0.003, "White", segs=16, d2=0.004)  # point
    bit.finish()
    Switch(unit, "WorkDrill", (sx + 0.025, 0, 0.004), UP)
    Lamp(u, "DrillUp", (0.035, 0.03, 0.075), FRONT)
    Lamp(u, "DrillDown", (0.035, 0.03, 0.05), FRONT)
    Switch(u, "DrillDown", (0.035, 0.03, 0.02), FRONT)


def conveyor(mps):
    rootY = 0.075
    u = Node("Conveyor", mps, (F(BeltX), rootY, F(BeltChipZ - ChipHalfT)))
    bottom = BaseTop - F(BeltChipZ - ChipHalfT)
    # Belt: top run + wraps around both rollers (r 0.02 at y +-0.42, z -0.02); top stays at z 0, length 0.88.
    belt = Part("ConveyorBelt", u, (0, 0, -0.002))
    cy, cz, m = 0.42, -0.018, 12
    def path(R):
        left = [(-cy + R * math.cos(t), cz + R * math.sin(t)) for t in [math.radians(270 - 180 * i / m) for i in range(m + 1)]]
        right = [(cy + R * math.cos(t), cz + R * math.sin(t)) for t in [math.radians(90 - 180 * i / m) for i in range(m + 1)]]
        return [(-cy + 0.02, cz - R)] + left + right + [(cy - 0.02, cz - R)]
    belt.ring(path(0.02), path(0.0175), "x", (0, 0, 0), 0.08, "Belt", closed=False)
    belt.finish()
    body = Part("ConveyorBody", u, (0, 0, -0.019))  # two slotted side rails + slider bed
    for s in (-1, 1):
        slotted(body, (s * 0.054, 0, 0), (0.022, 0.84, 0.03), "White", "y")
    body.box((0, 0, 0.0125), (0.086, 0.80, 0.003), "Purple", 0)
    body.finish()
    for s in (-1, 1):
        r = Part("Roller:%d" % (1 if s < 0 else 2), u, (0, s * 0.42, -0.02))
        r.cyl((0, 0, 0), "x", 0.034, 0.082, "White", segs=32, bev=0.001)
        r.cyl((0, 0, 0), "x", 0.008, 0.124, "Nut", segs=12)
        r.finish()
        for x in (-1, 1):
            gd = Part("RollerGuide:%d" % (s + 2 + (x + 1) // 2), u, (x * 0.053, s * 0.42, -0.02))
            gd.cyl((0, 0, 0), "x", 0.044, 0.006, "Yellow", segs=32, bev=0.001)
            gd.cyl((x * 0.004, 0, 0), "x", 0.016, 0.003, "Nut", segs=24)  # bearing hub
            gd.finish()
        h = -0.034 - bottom
        for k in range(3):
            leg = Part("ConveyorFrame:%d" % (k * 2 + (s + 3) // 2), u, (s * 0.035, (k - 1) * 0.36, (bottom - 0.034) / 2))
            slotted(leg, (0, 0, 0), (0.024, 0.024, h - 0.008), "CylinderBody", "z", w=0.004)
            leg.box((0, 0, -h / 2 + 0.002), (0.03, 0.06, 0.004), "CylinderBody", 0.0008)
            leg.box((0, 0, h / 2 - 0.002), (0.03, 0.06, 0.004), "CylinderBody", 0.0008)
            for j in (-1, 1):
                hexbolt(leg, (0, j * 0.022, -h / 2 + 0.0048), "z", 0.006)
            leg.finish()
        cv = Part("ConveyorFrameCover:%d" % (s + 2), u, (s * 0.068, 0.33, -0.015))
        cv.box((0, 0, 0), (0.006, 0.06, 0.035), "Purple", 0.0015)
        for j in (-1, 1):
            hexbolt(cv, (s * 0.0035, j * 0.022, 0.011), "x", 0.005)
        cv.finish()
    # Sorter bracket over the belt, metal/nonmetal sensors hanging under the lamps
    so = Node("ChipSorter", u, (0, F(SorterY) - rootY, 0))
    st = Part("SorterTop", so, (0, 0, 0.038))
    st.box((0, 0, 0), (0.14, 0.06, 0.01), "Purple", 0.0015)
    for x in (-0.018, 0.018):
        st.cyl((x, 0, -0.0075), "z", 0.012, 0.005, "Nut", segs=6)  # lock nut
        st.cyl((x, 0, -0.0115), "z", 0.010, 0.007, "CylinderBody", segs=24)  # M10 sensor barrel
        st.cyl((x, 0, -0.01625), "z", 0.009, 0.0025, "Belt", segs=24)  # sensing face, 10 mm above a chip
    st.finish()
    for nm, x in (("SorterLegL", -0.065), ("SorterLegR", 0.065)):
        lg = Part(nm, so, (x, 0, 0.0215))
        lg.box((0, 0, 0), (0.01, 0.06, 0.043), "Purple", 0.0015)
        for j in (-1, 1):
            hexbolt(lg, (math.copysign(0.0055, x), j * 0.02, -0.012), "x", 0.005)
        lg.finish()
    Lamp(so, "Metal", (-0.018, 0, 0.043), UP)
    Lamp(so, "Nonmetal", (0.018, 0, 0.043), UP)
    # Stopper: body beside the belt, finger across it; the unit moves in z (up = blocking)
    py = F(StopperY) - rootY
    br = Part("StopperBracket", u, (0.078, py, (bottom + 0.03) / 2))
    slotted(br, (0, 0, 0), (0.02, 0.06, 0.03 - bottom), "Purple", "z", w=0.004)
    br.finish()
    stop = Node("StopperUnit", u, (0, 0, F(StopperDown) - 0.065), mover=True)  # rest = down; the command raises it by 25 mm
    sc = Part("StopperCylinder", stop, (0.055, py, 0.035))
    sc.cyl((0, 0, 0), "z", 0.025, 0.042, "Yellow", segs=32, bev=0.001)
    for zz in (-0.0225, 0.0225):
        sc.cyl((0, 0, zz), "z", 0.027, 0.005, "Nut", segs=32, bev=0.0008)
    sc.finish()
    fy = F(StopY + ChipR) - rootY + 0.004
    fi = Part("StopperFinger", stop, (0.005, fy, 0.007))
    fi.box((0, 0.001, 0), (0.09, 0.006, 0.014), "Yellow", 0.0012)
    fi.box((0, -0.003, 0), (0.086, 0.002, 0.012), "Belt", 0.0004)  # rubber face: stop face unchanged
    fi.finish()
    Part("StopperArm", stop, (0.055, fy + 0.0085, 0.007)).box((0, 0, 0), (0.018, 0.013, 0.014), "Yellow", 0.001).finish()  # added
    Lamp(stop, "StopperDetect", (0.055, py, 0.06), UP)
    Lamp(u, "StopperUp", (0.078, py - 0.012, 0.03), UP)
    Lamp(u, "StopperDown", (0.078, py + 0.012, 0.03), UP)
    Switch(u, "StopperDown", (0.055, py + 0.05, 0), UP)
    Lamp(u, "ConveyorLamp", (-0.055, -0.315, 0), UP)
    Switch(u, "Conveyor", (-0.055, -0.286, 0), UP)


def open_box(mps, name, pos, s):
    t = Node(name, mps, pos)
    w = 0.004
    Part("Bottom", t, (0, 0, w / 2)).box((0, 0, 0), (s[0], s[1], w), "Cream", 0.001).finish()
    Part("WallXn", t, (-s[0] / 2 + w / 2, 0, s[2] / 2)).box((0, 0, 0), (w, s[1], s[2]), "Cream", 0.0015).finish()
    Part("WallXp", t, (s[0] / 2 - w / 2, 0, s[2] / 2)).box((0, 0, 0), (w, s[1], s[2]), "Cream", 0.0015).finish()
    Part("WallYn", t, (0, -s[1] / 2 + w / 2, s[2] / 2)).box((0, 0, 0), (s[0], w, s[2]), "Cream", 0.0015).finish()
    Part("WallYp", t, (0, s[1] / 2 - w / 2, s[2] / 2)).box((0, 0, 0), (s[0], w, s[2]), "Cream", 0.0015).finish()


def lift(mps):
    u = Node("Lift", mps, (F(LiftRootX), F(LiftRootY), 0))
    H = 0.1415 - BaseTop
    fr = Part("LiftFrame", u, (0, 0, (BaseTop + 0.1415) / 2))  # 80x80 slotted profile column
    slotted(fr, (0, 0, -0.0015), (0.08, 0.08, H - 0.003), "LightBlue", "z", n=2)
    fr.box((0, 0, H / 2 - 0.0015), (0.08, 0.08, 0.003), "Nut", 0.001)  # top cap, top stays at 0.1415
    fr.box((0, 0, -H / 2 + 0.003), (0.1, 0.1, 0.006), "Nut", 0.0015)  # foot plate
    for i in (-1, 1):
        for j in (-1, 1):
            hexbolt(fr, (i * 0.044, j * 0.044, -H / 2 + 0.0068), "z", 0.006)
    fr.box((-0.0415, 0, 0), (0.003, 0.02, H - 0.04), "Nut", 0.0005)  # linear rail for the carriage
    fr.finish()
    servo = Node("ServoLifter", u, (0, 0, F(ServoStartUm)), mover=True)
    py, pz = F(PadDy), F(PadDz)
    cb = Part("CylinderBaseShort", servo, (-0.065, py / 2 - 0.005, 0))
    slotted(cb, (0, 0, 0), (0.05, -py + 0.03, 0.05), "CylinderBody", "y")
    for i in (-1, 1):
        for j in (-1, 1):
            hexbolt(cb, (i * 0.018, j * 0.06, 0.0258), "z")
    cb.finish()
    sh = Part("CylinderShaft", servo, (-0.115, py, 0))
    sh.cyl((0, 0, 0), "x", 0.02, 0.05, "White", segs=32)
    sh.cyl((-0.024, 0, 0), "x", 0.024, 0.004, "Nut", segs=6)  # gland nut
    sh.finish()
    Lamp(servo, "SuctionPad", (-0.075, -0.025, 0.025), UP)
    Switch(servo, "SuctionPad", (-0.055, -0.025, 0.025), UP)
    inner = Node("CylinderLiftInner", servo, (F(LiftRet), 0, 0), mover=True)
    px = F(PadDx)
    Part("CylinderInnerShaft", inner, (px / 2 + 0.01, py, 0)).cyl((0, 0, 0), "x", 0.01, -px + 0.02, "White", segs=24).finish()
    vf = Part("VaccumFrame", inner, (px, py, pz + 0.0215))
    vf.box((0, 0, 0), (0.04, 0.03, 0.013), "LightBlue", 0.0015)
    vf.cyl((0.012, 0, 0.0085), "z", 0.006, 0.004, "Belt", segs=12)  # vacuum fitting
    vf.finish()
    vp = Part("VaccumPad", inner, (px, py, pz + 0.0075))  # bellows suction cup, lip at the pad tip
    vp.cyl((0, 0, 0.0055), "z", 0.012, 0.004, "Nut", segs=6)
    vp.cyl((0, 0, 0.002), "z", 0.022, 0.003, "Yellow", segs=32, bev=0.0005)
    vp.cyl((0, 0, -0.0015), "z", 0.028, 0.004, "Yellow", segs=32, bev=0.0005)
    vp.cyl((0, 0, -0.0055), "z", 0.04, 0.004, "Belt", segs=40, d2=0.026)
    vp.finish()
    Lamp(u, "LiftForward", (-0.02, -0.02, 0.1415), UP)
    Lamp(u, "LiftBackward", (0.02, -0.02, 0.1415), UP)
    Switch(u, "LiftForward", (-0.02, 0.02, 0.1415), UP)
    Switch(u, "LiftBackward", (0.02, 0.02, 0.1415), UP)
    Switch(u, "ServoUp", (-0.04, 0.022, 0.116), LEFT)
    Switch(u, "ServoDown", (-0.04, 0.022, -0.013), LEFT)
    Lamp(u, "ServoUp", (0, 0.04, 0.10), FRONT)
    Lamp(u, "ServoHome", (0, 0.04, 0.06), FRONT)
    Lamp(u, "ServoDown", (0, 0.04, 0.02), FRONT)


def stocker(mps):
    root = (F(StockerRootX), F(StockerRootY), -0.06)
    inner = cylinder_unit(mps, "CylinderStocker", "CylinderStockerInner", None, "Stocker", root, 1, -1, StockerRet, -0.025, 0)
    sx, sy, z0 = F(SlotDx), F(SlotDy), F(StockerZ) - root[2]
    sb = Part("ShelfBase", inner, (sx, sy, -0.06))
    sb.box((0, 0, 0), (0.07, 0.11, 0.008), "White", 0.0015)
    for i in (-1, 1):
        for j in (-1, 1):
            hexbolt(sb, (i * 0.015, j * 0.049, 0.0048), "z")
    sb.finish()
    for k in range(4):
        sf = Part("ShelfFrame:%d" % (k + 1), inner, (sx + (k % 2 * 2 - 1) * 0.027, sy + (k // 2 * 2 - 1) * 0.047, 0.03))
        sf.box((0, 0, 0), (0.006, 0.006, 0.18), "Orange", 0.0008)
        sf.box((0, 0, 0.0905), (0.008, 0.008, 0.003), "Nut", 0.0006)  # cap
        sf.finish()
    for k, zk in enumerate(ShelfZ):
        s = Part("Shelft:%d" % (k + 1), inner, (sx, sy, z0 + F(zk) - 0.0025))
        s.box((0, 0, 0), (0.06, 0.10, 0.005), "Orange", 0.001)
        for j in (-1, 1):
            s.box((0, j * 0.044, 0.0035), (0.044, 0.003, 0.003), "Orange", 0.0006)  # end lips (chip is ø40 at the centre)
        s.finish()
    br = Part("ShelfBracket", inner)  # added: yoke from the rod end to the rack (SceneBuilder left them unconnected)
    br.box((0.0315, -0.0125, 0.004), (0.073, 0.015, 0.012), "Orange", 0.0012)
    br.box((0.062, -0.0125, -0.026), (0.012, 0.015, 0.06), "Orange", 0.0012)
    br.finish()


def chips():
    """Chip prefabs (ø40 x 10, centre origin) at the origin, hidden in renders; exported for the prefab step."""
    for name in ("ChipMetal", "ChipNonmetal"):
        c = Part(name, None)
        c.cyl((0, 0, 0), "z", 0.04, 0.01, name, segs=40, bev=0.0008)
        c.finish()
        c.obj.hide_render = True


# ---------------------------------------------------------------- self-check (anchors the Sim relies on)
MOVERS = ["CylinerSupplyInner", "CylinderEmitInner", "DrillUnit", "CylinderDischargeInner", "StopperUnit", "CylinderLiftInner", "CylinderStockerInner"]
LAMPS = ["SupplyForward", "SupplyBackward", "EmitForward", "EmitBackward", "DrillDown", "DrillUp", "DischargeForward", "DischargeBackward",
         "StopperDown", "StopperUp", "LiftForward", "LiftBackward", "SuctionPad", "StockerForward", "StockerBackward", "SupplySensor",
         "Nonmetal", "Metal", "StopperDetect"]
SWITCHES = ["SupplyForward", "SupplyBackward", "EmitForward", "EmitBackward", "DrillDown", "DischargeForward", "DischargeBackward",
            "StopperDown", "Conveyor", "LiftForward", "LiftBackward", "ServoUp", "ServoDown", "StockerForward", "StockerBackward",
            "SuctionPad", "WorkDrill"]
BOUND = MOVERS + ["Lamp" + n for n in LAMPS] + ["Switch" + n for n in SWITCHES] + ["ServoLifter", "LampConveyorLamp", "LampServoUp", "LampServoHome", "LampServoDown"]


def check():
    bpy.context.view_layer.update()
    obj = bpy.data.objects
    missing = [n for n in BOUND if n not in obj]
    assert not missing, "missing bound names: %s" % missing
    assert len(set(BOUND)) == 48
    for n in BOUND:
        if n.startswith("Lamp") or n.startswith("Switch"):
            slots = [m.name for m in obj[n].data.materials]
            assert slots == ["LampOff" if n.startswith("Lamp") else "SwitchOff"], (n, slots)

    def mps(v):
        return Vector((-v.x, v.y, v.z))

    def bounds(n):
        o = obj[n]
        vs = [mps(o.matrix_world @ v.co) for v in o.data.vertices]
        return [(min(v[i] for v in vs), max(v[i] for v in vs)) for i in range(3)]

    def origin(n):
        return mps(obj[n].matrix_world.translation)

    # independent expected values (worked out by hand from MpsGeometry, metres)
    expect = [
        ("CylinerSupplyInner origin", origin("CylinerSupplyInner"), (-0.180, -0.377, -0.083)),
        ("CylinderEmitInner origin", origin("CylinderEmitInner"), (0.035, -0.522, -0.083)),
        ("DrillUnit origin", origin("DrillUnit"), (0.055, -0.399, 0.027)),
        ("CylinderDischargeInner origin", origin("CylinderDischargeInner"), (0.165, -0.006, -0.083)),
        ("StopperUnit origin", origin("StopperUnit"), (0.035, 0.075, -0.113)),
        ("CylinderLiftInner origin", origin("CylinderLiftInner"), (0.142, 0.344, 0.090)),
        ("CylinderStockerInner origin", origin("CylinderStockerInner"), (-0.085, 0.359, -0.060)),
        ("ServoLifter origin", origin("ServoLifter"), (0.172, 0.344, 0.090)),
    ]
    scal = [
        ("pad tip z (servo start + PadDz)", bounds("VaccumPad")[2][0], 0.062),
        ("pad centre x", sum(bounds("VaccumPad")[0]) / 2, 0.035),
        ("pad centre y (= StopY)", sum(bounds("VaccumPad")[1]) / 2, 0.234),
        ("stopper face y (StopY + ChipR)", bounds("StopperFinger")[1][0], 0.254),
        ("belt top z", bounds("ConveyorBelt")[2][1], -0.088),
        ("belt y extent", bounds("ConveyorBelt")[1][1] - bounds("ConveyorBelt")[1][0], 0.88),
        ("work table top z", bounds("WorkTableBlock")[2][1], -0.088),
        ("supply push face x (MagX - ChipR)", bounds("SupplyGripper")[0][1], -0.070),
        ("emit push face y (StageY - ChipR)", bounds("EmitGripper")[1][1], -0.4215),
        ("discharge push face x (BeltX + ChipR)", bounds("DischargeGripper")[0][0], 0.055),
        ("drill tip z at DrillUp", bounds("Drill:1")[2][0], -0.007),
        ("box1 floor z", bounds("Bottom")[2][0], BaseTop),
    ]
    for k, zk in enumerate(ShelfZ):
        o = obj["Shelft:%d" % (k + 1)]
        top = max(mps(o.matrix_world @ v.co).z for v in o.data.vertices if abs(v.co.x) > 0.025)  # plate edge, not the lips
        scal.append(("shelf %d plate top z" % (k + 1), top, F(StockerZ + zk)))
    bad = [(n, tuple(round(c, 6) for c in got), want) for n, got, want in expect if (got - Vector(want)).length > 1e-5]
    bad += [(n, round(got, 6), want) for n, got, want in scal if abs(got - want) > 1e-5]
    assert not bad, "anchor mismatch: %s" % bad
    print("CHECK ok: %d bound names, %d origins, %d anchors" % (len(BOUND), len(expect), len(scal)))


# ---------------------------------------------------------------- export, preview
def export():
    os.makedirs(OUT_MODEL, exist_ok=True)
    import json
    with open(os.path.join(OUT_MODEL, "layout.json"), "w") as f:  # Unity cannot read the FBX node frames reliably; the importer rebuilds the skeleton from this
        json.dump({"items": LAYOUT}, f)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT_MODEL, "mps.glb"), export_format="GLB", export_extras=True, export_yup=True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT_MODEL, "mps.fbx"), axis_forward="-Z", axis_up="Y",
                             apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True, object_types={"EMPTY", "MESH"},
                             use_custom_props=True, add_leaf_bones=False, mesh_smooth_type="FACE")


def u2b(v):
    return Vector((-v[0], -v[2], v[1]))  # Unity world -> Blender world


def m2b(v):
    return Vector((-v[0], v[1], v[2]))  # MPS-local -> Blender world


def unity_dir(ex, ey, v):
    """Unity Quaternion.Euler(ex, ey, 0) * v (applied x then y)."""
    ex, ey = math.radians(ex), math.radians(ey)
    x, y, z = v
    y, z = y * math.cos(ex) - z * math.sin(ex), y * math.sin(ex) + z * math.cos(ex)
    x, z = x * math.cos(ey) + z * math.sin(ey), -x * math.sin(ey) + z * math.cos(ey)
    return (x, y, z)


def camera(name, pos, fwd, up, ortho=None, lens=35):
    cd = bpy.data.cameras.new(name)
    if ortho:
        cd.type, cd.ortho_scale = "ORTHO", ortho
    else:
        cd.lens = lens
    cd.clip_start, cd.clip_end = 0.01, 100
    o = bpy.data.objects.new(name, cd)
    bpy.context.scene.collection.objects.link(o)
    f = fwd.normalized()
    r = f.cross(up).normalized()
    m = Matrix((r, r.cross(f), -f)).transposed()
    o.matrix_world = Matrix.Translation(pos) @ m.to_4x4()
    return o


def preview():
    sc = bpy.context.scene
    # preview-only metal chip on the stage, like the Unity screenshot (not exported)
    c = Part("PreviewChip", None, (F(StageX), F(StageY), F(TableChipZ)))
    c.cyl((0, 0, 0), "z", 0.04, 0.01, "ChipMetal", segs=40, bev=0.0008).finish()

    sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = 1387, 1000, 100
    sc.view_settings.view_transform = "Standard"
    bg = tuple(lin(c * 255) for c in (0.90, 0.91, 0.92)) + (1.0,)
    w = bpy.data.worlds.new("World")
    sc.world = w
    w.color = bg[:3]
    nt = w.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    amb, vis = nt.nodes.new("ShaderNodeBackground"), nt.nodes.new("ShaderNodeBackground")
    amb.inputs[0].default_value, amb.inputs[1].default_value = (0.62, 0.63, 0.66, 1), 0.55  # style A trilight ambient, roughly
    vis.inputs[0].default_value, vis.inputs[1].default_value = bg, 1.0
    mix, lp = nt.nodes.new("ShaderNodeMixShader"), nt.nodes.new("ShaderNodeLightPath")
    nt.links.new(lp.outputs["Is Camera Ray"], mix.inputs[0])
    nt.links.new(amb.outputs[0], mix.inputs[1])
    nt.links.new(vis.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs[0])

    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy, sun.data.angle = 2.6, math.radians(8)
    sc.collection.objects.link(sun)
    sun.rotation_euler = u2b(unity_dir(50, -30, (0, 0, 1))).to_track_quat("-Z", "Y").to_euler()  # Unity default light

    if ENGINE == "WORKBENCH":
        sc.render.engine = "BLENDER_WORKBENCH"
        sh = sc.display.shading
        sh.light, sh.color_type, sh.show_shadows, sh.show_cavity = "STUDIO", "MATERIAL", True, True
        sh.background_type = "WORLD"
    else:
        sc.render.engine = "BLENDER_EEVEE"
        sc.eevee.taa_render_samples = 32

    # same pose as SceneBuilder: pos (-0.275, 0.411, -0.739), Euler(20, 20, 0), ortho size 0.5 (half height) -> width 1.387
    shots = [
        ("a_default", camera("CamDefault", u2b((-0.275, 0.411, -0.739)), u2b(unity_dir(20, 20, (0, 0, 1))),
                             u2b(unity_dir(20, 20, (0, 1, 0))), ortho=2 * 0.5 * 1387 / 1000)),
        ("a_front", camera("CamFront", m2b((-1.15, 0.30, 0.10)), m2b((0.02, 0.0, -0.10)) - m2b((-1.15, 0.30, 0.10)), Z, lens=32)),
        ("a_top", camera("CamTop", m2b((0.0, 0.0, 2.0)), Vector((0, 0, -1)), m2b((1, 0, 0)), ortho=1.32)),
    ]
    os.makedirs(OUT_PREV, exist_ok=True)
    for name, cam in shots:
        sc.camera = cam
        sc.render.filepath = os.path.join(OUT_PREV, name + ".png")
        t0 = time.time()
        bpy.ops.render.render(write_still=True)
        print("RENDER %s %.1f s (%s)" % (name, time.time() - t0, sc.render.engine))


def main():
    global COL
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    COL = bpy.data.collections.new("MPS")
    bpy.context.scene.collection.children.link(COL)
    build()
    chips()
    check()
    export()
    os.makedirs(OUT_MODEL, exist_ok=True)
    print("BUILD+EXPORT %.1f s, %d objects" % (time.time() - t0, len(COL.objects)))
    if "--no-render" not in ARGS:
        preview()
    bpy.context.preferences.filepaths.save_version = 0  # no mps.blend1
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_MODEL, "mps.blend"))


main()
