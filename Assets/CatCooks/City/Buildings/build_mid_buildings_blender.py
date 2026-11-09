import bpy, bmesh, math, os
from mathutils import Vector, Matrix
OUT='/home/claude/mid/out'; os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
img=bpy.data.images.load('/home/claude/mid/atlas.png')
MAT=bpy.data.materials.new('Material'); MAT.use_nodes=True
tn=MAT.node_tree.nodes.new('ShaderNodeTexImage'); tn.image=img; tn.interpolation='Closest'
MAT.node_tree.links.new(tn.outputs[0], MAT.node_tree.nodes['Principled BSDF'].inputs['Base Color'])
S=1932.0
def tile(c,r,y0=0.0,y1=1.0,x0=0.0,x1=1.0):
    """прямоугольник UV внутри плитки атласа (c — столбец, r — ряд сверху). y0..y1 — доля плитки снизу вверх"""
    u0=c/4+ x0/4; u1=c/4+x1/4; v0=1-(r+1)/4 + y0/4; v1=1-(r+1)/4 + y1/4
    return (u0,v0,u1,v1)
def sw2(px,py):  # точка в координатах исходного атласа 2048
    u=px/2048; v=1-py/2048; e=1/2048
    return (u,v,u+e,v+e)
def sw(px,py):  # сплошной цвет: точка пикселя атласа
    u=px/S; v=1-py/S; e=1/S
    return (u,v,u+e,v+e)
# ── палитра плиток
T={
 'brick_or_win': tile(0,0,0.25,1), 'brick_or_base': tile(0,0,0,0.25), 'brick_or_plain': tile(1,0,0.3,1),
 'red_arch_win': tile(2,0,0.3,0.82), 'red_door': tile(2,0,0,0.28), 'red_plain': tile(3,0,0.1,1),
 'red_win': tile(0,1,0.3,1), 'grey_arch': tile(1,1,0.15,1), 'grey_base': tile(1,1,0,0.15),
 'cream_arch': tile(2,1,0.33,1), 'cream_shop': tile(2,1,0,0.33), 'orange_win': tile(3,1,0,1),
 'orange_win2': tile(0,2,0.45,1), 'orange_shop': tile(0,2,0,0.42), 'stone_win': tile(1,2,0.25,1), 'stone_door': tile(1,2,0,0.25),
 'stone_plain': tile(2,2,0.25,1), 'brown_pair': tile(3,2,0,1), 'stone_arch_base': tile(0,3,0,0.66),
 'glass_cream': tile(1,3,0,1), 'glass_dark': tile(2,3,0,1),
 'teal': sw2(1545,1793), 'bluegrey': sw2(1537,1921), 'lightgrey': sw2(1600,1640), 'red': sw(1660,1480), 'brown': sw2(1857,1560),
 'purple': sw(1840,1480), 'darkpurple': sw(1900,1480), 'cream': sw2(1097,560), 'stone': sw2(1300,1250), 'dark': sw2(1625,1565),
 'rose': tile(3,3,0.88,1,0.0,0.25),
}
class B:
    def __init__(s,name):
        s.name=name; s.bm=bmesh.new(); s.uv=s.bm.loops.layers.uv.new('UVMap')
    def quad(s,a,b,c,d,uvr,uvq=None):
        vs=[s.bm.verts.new(p) for p in (a,b,c,d)]
        f=s.bm.faces.new(vs)
        u0,v0,u1,v1=uvr
        q=uvq or [(u0,v0),(u1,v0),(u1,v1),(u0,v1)]
        for l,uv in zip(f.loops,q): l[s.uv].uv=uv
        return f
    def tri(s,a,b,c,uvr):
        f=s.bm.faces.new([s.bm.verts.new(p) for p in (a,b,c)])
        u0,v0,u1,v1=uvr
        for l,uv in zip(f.loops,[(u0,v0),(u1,v0),((u0+u1)/2,v1)]): l[s.uv].uv=uv
    def wall(s,p0,p1,y0,h,uvr,cw=16,ch=16):
        """стена от p0 до p1 (по земле, x,y Blender), снизу y0 высотой h; сетка плиток примерно cw×ch м"""
        p0=Vector(p0); p1=Vector(p1); L=(p1-p0).length
        nx=max(1,round(L/cw)); ny=max(1,round(h/ch))
        for i in range(nx):
            for j in range(ny):
                a=p0.lerp(p1,i/nx); b=p0.lerp(p1,(i+1)/nx); z0=y0+h*j/ny; z1=y0+h*(j+1)/ny
                s.quad((a.x,a.y,z0),(b.x,b.y,z0),(b.x,b.y,z1),(a.x,a.y,z1),uvr)
    def ring(s,pts,y0,h,uvr,cw=16,ch=16):
        n=len(pts)
        for i in range(n): s.wall(pts[i],pts[(i+1)%n],y0,h,uvr,cw,ch)
    def cap(s,pts,z,uvr):
        vs=[s.bm.verts.new((p[0],p[1],z)) for p in pts]
        f=s.bm.faces.new(vs)
        for l in f.loops: l[s.uv].uv=(uvr[0],uvr[1])
    def box(s,cx,cy,w,d,y0,h,uvr,roof='lightgrey',cw=16,ch=16):
        pts=[(cx-w/2,cy-d/2),(cx+w/2,cy-d/2),(cx+w/2,cy+d/2),(cx-w/2,cy+d/2)]
        s.ring(pts,y0,h,uvr,cw,ch); s.cap(pts,y0+h,T[roof]); return pts
    def cornice(s,pts,z,out=0.9,h=1.2,uvr=None):
        z+=0.05
        uvr=uvr or T['cream']
        c=Vector((sum(p[0] for p in pts)/len(pts),sum(p[1] for p in pts)/len(pts)))
        big=[tuple((Vector(p)-c)*(1+out/max(1,(Vector(p)-c).length))+c) for p in pts]
        s.ring(big,z,h,uvr,999,999)
        # верх карниза — только кольцо (без перекрытия крыши: иначе две плоскости друг на друге)
        n=len(pts); zt=z+h
        for i in range(n):
            ia=pts[i]; ib=pts[(i+1)%n]; oa=big[i]; ob=big[(i+1)%n]
            s.quad((ia[0],ia[1],zt),(oa[0],oa[1],zt),(ob[0],ob[1],zt),(ib[0],ib[1],zt),(uvr[0],uvr[1],uvr[0],uvr[1]))
    def pyramid(s,cx,cy,w,d,z,h,uvr):
        a=[(cx-w/2,cy-d/2),(cx+w/2,cy-d/2),(cx+w/2,cy+d/2),(cx-w/2,cy+d/2)]; top=(cx,cy,z+h)
        for i in range(4):
            p=a[i]; q=a[(i+1)%4]; s.tri((p[0],p[1],z),(q[0],q[1],z),top,uvr)
    def gable(s,cx,cy,w,d,z,h,uvr,along_x=True):
        if along_x:
            A=(cx-w/2,cy-d/2,z);B_=(cx+w/2,cy-d/2,z);C=(cx+w/2,cy+d/2,z);D=(cx-w/2,cy+d/2,z);R0=(cx-w/2,cy,z+h);R1=(cx+w/2,cy,z+h)
            s.quad(A,B_,R1,R0,uvr); s.quad(C,D,R0,R1,uvr); s.tri(B_,C,R1,uvr); s.tri(D,A,R0,uvr)
        else:
            A=(cx-w/2,cy-d/2,z);B_=(cx+w/2,cy-d/2,z);C=(cx+w/2,cy+d/2,z);D=(cx-w/2,cy+d/2,z);R0=(cx,cy-d/2,z+h);R1=(cx,cy+d/2,z+h)
            s.quad(B_,C,R1,R0,uvr); s.quad(D,A,R0,R1,uvr); s.tri(A,B_,R0,uvr); s.tri(C,D,R1,uvr)
    def cyl(s,cx,cy,r,y0,h,uvr,n=16,cw=12,ch=16,roof='lightgrey',dome=None,domeH=0):
        pts=[(cx+math.cos(i/n*2*math.pi)*r, cy+math.sin(i/n*2*math.pi)*r) for i in range(n)]
        ny=max(1,round(h/ch)); per=2*math.pi*r/n; cells=max(1,round(2*math.pi*r/cw))
        for i in range(n):
            a=pts[i]; b=pts[(i+1)%n]
            for j in range(ny):
                z0=y0+h*j/ny; z1=y0+h*(j+1)/ny
                u0,v0,u1,v1=uvr; ua=u0+(u1-u0)*((i*cells/n)%1); ub=ua+(u1-u0)*(cells/n)
                if ub>u1+1e-6: ua,ub=u0,u0+(u1-u0)*(cells/n)
                s.quad((a[0],a[1],z0),(b[0],b[1],z0),(b[0],b[1],z1),(a[0],a[1],z1),uvr,[(ua,v0),(ub,v0),(ub,v1),(ua,v1)])
        if dome:
            top=(cx,cy,y0+h+domeH)
            for i in range(n):
                a=pts[i]; b=pts[(i+1)%n]; s.tri((a[0],a[1],y0+h),(b[0],b[1],y0+h),top,T[dome])
        else: s.cap(pts,y0+h,T[roof])
    def tank(s,cx,cy,z):
        s.box(cx,cy,4,4,z,3,T['dark'],'dark',99,99)
        s.cyl(cx,cy,2.4,z+3,4.5,T['brown'],n=10,cw=99,ch=99,dome='dark',domeH=1.8)
    def finish(s):
        # порядок вершин везде против часовой снаружи — нормали правильные без пересчёта
        me=bpy.data.meshes.new(s.name); s.bm.to_mesh(me); s.bm.free()
        ob=bpy.data.objects.new(s.name,me); bpy.context.scene.collection.objects.link(ob); me.materials.append(MAT)
        return ob

objs=[]
# 1 Флэтайрон — клин
b=B('CC_Mid_Flatiron'); tri_pts=[(-22,-14),(22,-14),(-4,26)]
b.ring(tri_pts,0,6,T['cream_shop'],14,6); b.ring(tri_pts,6,62,T['cream_arch'],14,16)
b.cornice(tri_pts,26,0.6,0.8); b.cornice(tri_pts,68,1.2,1.6); b.cap(tri_pts,69.6,T['lightgrey'])
b.box(-6,-4,8,6,69.6,4,T['stone_plain'],'dark',99,99); objs.append(b.finish())
# 2 Собор — неф, трансепт, две башни со шпилями, роза
b=B('CC_Mid_Cathedral')
b.box(0,8,22,56,0,24,T['stone_plain'],'stone',12,12); b.gable(0,8,22,56,24,12,T['bluegrey'],False)
b.box(0,18,52,14,0,22,T['stone_plain'],'stone',12,12); b.gable(0,18,52,14,22,10,T['bluegrey'],True)
for x in (-8,8):
    tp=b.box(x,-24,10,10,0,52,T['stone_win'],'stone',10,13); b.cornice(tp,52,0.5,1)
    b.pyramid(x,-24,10,10,53,26,T['bluegrey'])
b.quad((-6,-20.1,24),(6,-20.1,24),(6,-20.1,34),(-6,-20.1,34),T['rose'])
b.box(0,-21,12,2,0,18,T['stone_arch_base'],'stone',12,18); b.pyramid(0,36,8,8,24,20,T['teal'])
objs.append(b.finish())
# 3 Ар-деко с уступами
b=B('CC_Mid_ArtDecoSetback')
w=40; z=0
b.box(0,0,46,46,0,8,T['stone_door'],'lightgrey',16,8); z=8
for i,(hh,mat) in enumerate([(60,'stone_win'),(30,'stone_win'),(24,'cream_arch'),(16,'stone_win')]):
    pts=b.box(0,0,w,w,z,hh,T[mat],'lightgrey',14,16); b.cornice(pts,z+hh-0.8,0.7,0.8); z+=hh; w*=0.78
b.pyramid(0,0,w,w,z,12,T['dark']); b.box(0,0,1.2,1.2,z+10,24,T['dark'],'dark',99,99)
objs.append(b.finish())
# 4 Стеклянная башня на подиуме
b=B('CC_Mid_GlassPodium')
pts=b.box(0,0,50,40,0,12,T['cream_shop'],'lightgrey',16,12); b.cornice(pts,11.5,0.8,0.8)
pts=b.box(4,0,30,26,12,110,T['glass_dark'],'dark',20,20); b.box(4,0,30.6,26.6,70,2,T['dark'],'dark',99,99)
b.box(4,0,14,12,122,6,T['dark'],'dark',99,99); objs.append(b.finish())
# 5 Кирпичный склад-лофт с бочкой
b=B('CC_Mid_BrickWarehouse')
pts=b.box(0,0,40,28,0,6,T['brick_or_base'],'lightgrey',16,6); pts=b.box(0,0,40,28,6,28,T['red_win'],'lightgrey',16,14)
b.cornice(pts,33,0.7,1.2); b.tank(10,4,34.2); b.box(-10,-5,6,5,34.2,4,T['red_plain'],'dark',99,99); objs.append(b.finish())
# 6 Чугунный лофт
b=B('CC_Mid_CastIronLoft')
pts=b.box(0,0,30,40,0,5,T['grey_base'],'lightgrey',15,5); pts=b.box(0,0,30,40,5,30,T['grey_arch'],'lightgrey',15,15)
b.cornice(pts,12,0.4,0.6,T['dark']); b.cornice(pts,24,0.4,0.6,T['dark']); b.cornice(pts,35,1.4,2.0,T['dark']); objs.append(b.finish())
# 7 Дом с круглым углом
b=B('CC_Mid_RoundCorner')
for (cx,cy,w,d) in [(-6,14,34,16),(14,-6,16,34)]:
    b.box(cx,cy,w,d,0,6,T['orange_shop'],'lightgrey',16,6); p=b.box(cx,cy,w,d,6,40,T['orange_win'],'lightgrey',16,16); b.cornice(p,45.5,0.7,1.0)
b.cyl(0,0,12,0,6,T['orange_shop'],n=16,cw=16,ch=6); b.cyl(0,0,12,6,46,T['orange_win'],n=16,cw=16,ch=16,dome='teal',domeH=10)
objs.append(b.finish())
# 8 Отель с мансардой и башенками
b=B('CC_Mid_MansardHotel')
pts=b.box(0,0,48,30,0,7,T['cream_shop'],'lightgrey',16,7); pts=b.box(0,0,48,30,7,40,T['cream_arch'],'lightgrey',16,14)
b.cornice(pts,46.5,0.9,1.0); b.gable(0,0,46,28,47.5,10,T['teal'],True)
for x in (-24,24):
    for y in (-15,15): b.cyl(x,y,4,0,52,T['cream_arch'],n=10,cw=10,ch=14,dome='teal',domeH=9)
for x in (-14,-4,6,16): b.box(x,-12.5,4,3,48,4,T['cream'],'teal',99,99)
objs.append(b.finish())
# 9 Двойные башни с «храмиками»
b=B('CC_Mid_TwinTowers')
pts=b.box(0,0,50,30,0,44,T['brown_pair'],'lightgrey',16,14); b.cornice(pts,43,0.9,1.0)
for x in (-14,14):
    p2=b.box(x,0,14,14,45,34,T['brown_pair'],'lightgrey',14,17); b.cornice(p2,78,0.6,0.8)
    for dx in (-5,5):
        for dy in (-5,5): b.box(x+dx,dy,1.2,1.2,79,7,T['cream'],'cream',99,99)
    b.pyramid(x,0,12,12,86,8,T['teal'])
objs.append(b.finish())
# 10 Часовая башня
b=B('CC_Mid_ClockTower')
pts=b.box(0,0,44,30,0,6,T['stone_door'],'lightgrey',15,6); pts=b.box(0,0,44,30,6,24,T['stone_win'],'lightgrey',15,12); b.cornice(pts,29.5,0.8,1.0)
p2=b.box(0,0,14,14,30,46,T['stone_plain'],'lightgrey',14,23); b.cornice(p2,75.5,0.6,1.0)
b.quad((-5,-7.05,62),(5,-7.05,62),(5,-7.05,72),(-5,-7.05,72),T['rose'])
b.quad((5,7.05,62),(-5,7.05,62),(-5,7.05,72),(5,7.05,72),T['rose'])
b.pyramid(0,0,14,14,77,18,T['teal']); objs.append(b.finish())
# 11 П-образный дом с двором
b=B('CC_Mid_UCourtyard')
for (cx,cy,w,d) in [(0,-16,50,14),(-18,4,14,26),(18,4,14,26)]:
    b.box(cx,cy,w,d,0,6,T['stone_arch_base'],'lightgrey',14,6); p=b.box(cx,cy,w,d,6,32,T['red_arch_win'],'lightgrey',14,16); b.cornice(p,37.5,0.7,1.0)
b.tank(-18,10,39); objs.append(b.finish())

# экспорт и превью
for o in objs:
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active=o
    bpy.ops.export_scene.fbx(filepath=f'{OUT}/{o.name}.fbx', use_selection=True, axis_forward='-Z', axis_up='Y',
                             apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, path_mode='STRIP', mesh_smooth_type='FACE')
import math as m
for i,o in enumerate(objs):
    col=i%6; row=i//6
    o.location=(col*85, row*110, 0); o.rotation_euler=(0,0,m.radians(-25))
sc=bpy.context.scene
sc.render.engine='CYCLES'; sc.cycles.samples=16; sc.cycles.device='CPU'; sc.cycles.use_denoising=False
w=bpy.data.worlds.new('w'); sc.world=w; w.use_nodes=True; w.node_tree.nodes['Background'].inputs['Color'].default_value=(0.62,0.78,0.92,1)
sun=bpy.data.lights.new('s','SUN'); sun.energy=3.2; sun.color=(1,0.93,0.82); so=bpy.data.objects.new('s',sun); sc.collection.objects.link(so); so.rotation_euler=(m.radians(50),0,m.radians(40))
bpy.ops.mesh.primitive_plane_add(size=2000,location=(200,60,0)); g=bpy.context.object
gm=bpy.data.materials.new('g'); gm.use_nodes=True; gm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(0.55,0.5,0.45,1); g.data.materials.append(gm)
sc.render.resolution_x=1900; sc.render.resolution_y=1100
cam=bpy.data.cameras.new('c'); cam.lens=35
co=bpy.data.objects.new('c',cam); sc.collection.objects.link(co); sc.camera=co
co.location=(215,-420,230); co.rotation_euler=(m.radians(66),0,0)
sc.render.filepath='/home/claude/mid/mine.png'; bpy.ops.render.render(write_still=True)
for o in objs:
    o.data.calc_loop_triangles(); print('BLD',o.name,len(o.data.loop_triangles))
