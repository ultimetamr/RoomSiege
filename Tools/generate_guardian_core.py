"""Author the RoomSiege guardian core as editable metre-scale OBJ mesh parts."""
from pathlib import Path
import ast
import json
import math

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/Scan/Resources/GuardianCore'

# Share the existing authoring primitives without running its twelve-device batch.
source = (ROOT / 'Tools/generate_support_devices.py').read_text(encoding='utf-8')
definition = next(node for node in ast.parse(source).body if isinstance(node, ast.ClassDef) and node.name == 'Mesh')
namespace = {'math': math, 'TAU': math.tau}
exec(compile(ast.Module(body=[definition], type_ignores=[]), '<shared mesh authoring>', 'exec'), namespace)
Mesh = namespace['Mesh']


def stable_tube(self, points, r=.007, mat='Trim', sides=8):
    # Parallel-transport the cross-section across bends: changing reference axes
    # at vertical joints creates twisted, pinched support arms.
    def dot(a, b): return sum(x * y for x, y in zip(a, b))
    def cross(a, b): return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]
    def unit(v):
        length = math.sqrt(dot(v, v))
        return [x / length for x in v]
    rings, previous = [], None
    for index, point in enumerate(points):
        before, after = points[max(0,index-1)], points[min(len(points)-1,index+1)]
        direction = unit([after[i]-before[i] for i in range(3)])
        if previous is None:
            reference = (0,1,0) if abs(direction[1]) < .9 else (1,0,0)
            normal = unit(cross(direction, reference))
        else:
            normal = unit([previous[i]-direction[i]*dot(previous,direction) for i in range(3)])
        tangent = cross(direction, normal); previous = normal
        rings.append([tuple(point[k]+r*(math.cos(i*math.tau/sides)*normal[k]+math.sin(i*math.tau/sides)*tangent[k])
                            for k in range(3)) for i in range(sides)])
    for index in range(len(rings)-1):
        for i in range(sides):
            next_index = (i+1) % sides
            self.face([rings[index][i], rings[index][next_index], rings[index+1][next_index], rings[index+1][i]], mat)
    self.face(rings[0][::-1], mat); self.face(rings[-1], mat)


Mesh.tube = stable_tube


def write(mesh):
    lines = ['# Original RoomSiege guardian core, units: metres', 'mtllib core-materials.mtl', 'o ' + mesh.name]
    lines += ['v %.6f %.6f %.6f' % point for point in mesh.v]
    lines += ['s off']
    material = None
    for group, indices in mesh.f:
        if group != material:
            lines.append('usemtl ' + group)
            material = group
        lines.append('f ' + ' '.join(map(str, indices)))
    (OUT / (mesh.name + '.obj')).write_text('\n'.join(lines) + '\n', encoding='utf-8')
    return {'part': mesh.name, 'triangles': sum(len(face) - 2 for _, face in mesh.f)}


def build():
    OUT.mkdir(parents=True, exist_ok=True)
    chassis = Mesh('GuardianChassis')
    chassis.lathe([(0, 0), (.115, 0), (.142, .009), (.147, .022), (.147, .034),
                   (.133, .047), (.111, .052), (0, .052)], 'Metal', 48)
    chassis.lathe([(.142, .024), (.1475, .028), (.1475, .032), (.142, .036)], 'Glow', 48)
    chassis.lathe([(0, .052), (.102, .052), (.112, .061), (.108, .075), (.09, .084), (0, .084)], 'Ceramic', 12)
    chassis.lathe([(.088, .081), (.099, .084), (.099, .09), (.088, .094)], 'Gold', 48)
    chassis.lathe([(0, .085), (.067, .085), (.072, .102), (.055, .114), (0, .114)], 'Inset', 24)
    chassis.torus((0, .106, 0), .061, .004, 'Glow', n=48, m=8)
    for index in range(3):
        angle = index * math.tau / 3
        sx, sz = math.sin(angle), math.cos(angle)
        # A tapered three-prong cradle leaves the crystal visible from all sides.
        points = [(sx * radius, height, sz * radius) for radius, height in
                  [(.112, .055), (.127, .10), (.127, .205), (.107, .264), (.08, .291)]]
        chassis.tube(points, .014, 'Metal', 8)
        chassis.tube([(x * .97, y, z * .97) for x, y, z in points[1:]], .003, 'Glow', 6)
        chassis.box((sx * .125, .168, sz * .125), (.031, .095, .029), 'Ceramic', math.degrees(angle), .005)
        chassis.box((sx * .125, .215, sz * .125), (.023, .012, .032), 'Gold', math.degrees(angle), .003)
        chassis.crystal((sx * .09, .283, sz * .09), .014, .047, 'Trim', 5)
    chassis.radial(12, .132, .046, (.015, .009, .012), 'Trim')
    chassis.radial(12, .137, .016, (.013, .006, .007), 'Inset', .15)

    heart = Mesh('GuardianHeart')
    heart.crystal((0, .255, 0), .054, .205, 'Crystal', 6)
    heart.crystal((0, .255, 0), .022, .22, 'Glow', 6)
    # Fine facet seams connect the point and shoulder instead of a generic cube.
    for index in range(6):
        a = index * math.tau / 6
        heart.tube([(0, .1935, 0), (.054 * math.cos(a), .22425, .054 * math.sin(a)),
                    (.0459 * math.cos(a), .3165, .0459 * math.sin(a)), (0, .3985, 0)], .0015, 'Glow', 4)

    orbit = Mesh('GuardianOrbit')
    for index in range(3):
        start = index * math.tau / 3
        points = [(.097 * math.cos(start + step * .025), .28, .097 * math.sin(start + step * .025)) for step in range(58)]
        orbit.tube(points, .0035, 'Gold', 8)
        orbit.tube([(x * .92, y - .011, z * .92) for x, y, z in points], .002, 'Glow', 6)
    result = [write(part) for part in (chassis, heart, orbit)]
    colors = {'Metal': (.075, .12, .18), 'Ceramic': (.19, .28, .36), 'Inset': (.015, .03, .045),
              'Trim': (.58, .71, .8), 'Gold': (1, .824, .478), 'Crystal': (.16, .69, .91), 'Glow': (.31, .847, 1)}
    (OUT / 'core-materials.mtl').write_text('\n\n'.join(
        f'newmtl {name}\nKd {r} {g} {b}\nKs 0.6 0.6 0.6\nNs 60\nd 1' for name, (r, g, b) in colors.items()) + '\n', encoding='utf-8')
    (OUT / 'model-info.json').write_text(json.dumps({'authorship': 'Original mesh authoring; no external 3D service', 'units': 'metres', 'parts': result}, indent=2), encoding='utf-8')
    print(json.dumps(result))


if __name__ == '__main__':
    build()
