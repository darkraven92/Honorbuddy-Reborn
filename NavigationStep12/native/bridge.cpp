// Internal C ABI for the VMaNGOS Detour variant. No game/client input.
#include "DetourNavMesh.h"
#include "DetourNavMeshQuery.h"
#include "DetourAlloc.h"
#include <cmath>
#include <cstring>
#include <memory>
#include <vector>
#include <algorithm>
#include <cstdint>
static_assert(sizeof(dtPolyRef) == 8, "VMaNGOS tiles require 64-bit polygon references");
static_assert(sizeof(dtNavMeshParams) == 28);
struct Context { dtNavMesh mesh; dtNavMeshQuery query; };
static bool ok(dtStatus s) { return dtStatusSucceed(s) && !(s & (DT_PARTIAL_RESULT | DT_BUFFER_TOO_SMALL | DT_OUT_OF_NODES)); }
static bool finite(const float* p) { return std::isfinite(p[0]) && std::isfinite(p[1]) && std::isfinite(p[2]); }
extern "C" {
void* hb_nav_create(const unsigned char* data, int size) {
    try {
        if (!data || size != sizeof(dtNavMeshParams)) return nullptr;
        dtNavMeshParams p; std::memcpy(&p, data, sizeof(p));
        if (!finite(p.orig) || !std::isfinite(p.tileWidth) || !std::isfinite(p.tileHeight) ||
            p.tileWidth <= 0 || p.tileHeight <= 0 || p.maxTiles < 1 || p.maxTiles > 4096 || p.maxPolys < 0) return nullptr;
        auto c = std::make_unique<Context>();
        if (!ok(c->mesh.init(&p)) || !ok(c->query.init(&c->mesh, 65535))) return nullptr;
        return c.release();
    } catch (...) { return nullptr; }
}
void hb_nav_destroy(void* context) { delete static_cast<Context*>(context); }
int hb_nav_add_tile(void* context, const unsigned char* data, int size) {
    if (!context || !data || size < int(sizeof(dtMeshHeader))) return 0;
    dtMeshHeader h; std::memcpy(&h, data, sizeof(h));
    if (h.magic != DT_NAVMESH_MAGIC || h.version != DT_NAVMESH_VERSION || h.polyCount <= 0 ||
        h.vertCount < 3 || h.maxLinkCount < 0 || h.detailMeshCount < 0 || h.detailVertCount < 0 ||
        h.detailTriCount < 0 || h.bvNodeCount < 0 || h.offMeshConCount < 0 || h.offMeshBase < 0 ||
        h.offMeshBase > h.polyCount || !finite(h.bmin) || !finite(h.bmax)) return 0;
    auto align = [](uint64_t n) { return (n + 3) & ~uint64_t(3); };
    uint64_t expected = align(sizeof(h)) + align(uint64_t(h.vertCount)*12) + align(uint64_t(h.polyCount)*sizeof(dtPoly)) +
        align(uint64_t(h.maxLinkCount)*sizeof(dtLink)) + align(uint64_t(h.detailMeshCount)*sizeof(dtPolyDetail)) +
        align(uint64_t(h.detailVertCount)*12) + align(uint64_t(h.detailTriCount)*4) +
        align(uint64_t(h.bvNodeCount)*sizeof(dtBVNode)) + align(uint64_t(h.offMeshConCount)*sizeof(dtOffMeshConnection));
    if (expected != uint64_t(size)) return 0;
    if (h.offMeshBase + int64_t(h.offMeshConCount) != h.polyCount || h.detailMeshCount != h.offMeshBase ||
        !std::isfinite(h.walkableHeight) || !std::isfinite(h.walkableRadius) || !std::isfinite(h.walkableClimb) ||
        !std::isfinite(h.bvQuantFactor) || h.bvQuantFactor <= 0) return 0;
    for (int i=0;i<3;++i) if (h.bmin[i]>h.bmax[i]) return 0;
    // Detour addTile assumes structurally valid payloads; validate all indexed arrays first.
    const auto* verts = reinterpret_cast<const float*>(data+align(sizeof(h)));
    const auto* polys = reinterpret_cast<const dtPoly*>(reinterpret_cast<const unsigned char*>(verts)+align(uint64_t(h.vertCount)*12));
    const auto* details = reinterpret_cast<const dtPolyDetail*>(reinterpret_cast<const unsigned char*>(polys)+
        align(uint64_t(h.polyCount)*sizeof(dtPoly))+align(uint64_t(h.maxLinkCount)*sizeof(dtLink)));
    const auto* detailVerts = reinterpret_cast<const float*>(reinterpret_cast<const unsigned char*>(details)+align(uint64_t(h.detailMeshCount)*sizeof(dtPolyDetail)));
    const auto* triangles = reinterpret_cast<const unsigned char*>(detailVerts)+align(uint64_t(h.detailVertCount)*12);
    const auto* nodes = reinterpret_cast<const dtBVNode*>(triangles+align(uint64_t(h.detailTriCount)*4));
    const auto* connections = reinterpret_cast<const dtOffMeshConnection*>(reinterpret_cast<const unsigned char*>(nodes)+align(uint64_t(h.bvNodeCount)*sizeof(dtBVNode)));
    for (int i=0;i<h.vertCount;++i) if (!finite(verts+i*3)) return 0;
    for (int i=0;i<h.detailVertCount;++i) if (!finite(detailVerts+i*3)) return 0;
    for (int i=0;i<h.polyCount;++i) {
        const auto& poly=polys[i];
        if (poly.vertCount>DT_VERTS_PER_POLYGON || poly.vertCount<(i<h.offMeshBase?3:2) ||
            poly.getType()!=(i<h.offMeshBase?DT_POLYTYPE_GROUND:DT_POLYTYPE_OFFMESH_CONNECTION)) return 0;
        for (int j=0;j<poly.vertCount;++j)
            if (poly.verts[j]>=h.vertCount ||
                ((poly.neis[j]&DT_EXT_LINK) ? (poly.neis[j]&0xff)>7 : poly.neis[j]>h.polyCount)) return 0;
    }
    for (int i=0;i<h.detailMeshCount;++i) {
        const auto& detail=details[i];
        if (uint64_t(detail.vertBase)+detail.vertCount>uint64_t(h.detailVertCount) ||
            uint64_t(detail.triBase)+detail.triCount>uint64_t(h.detailTriCount)) return 0;
        for (int j=0;j<detail.triCount;++j)
            for (int k=0;k<3;++k)
                if (triangles[(detail.triBase+j)*4+k]>=polys[i].vertCount+detail.vertCount) return 0;
    }
    for (int i=0;i<h.bvNodeCount;++i)
        if (nodes[i].i>=h.offMeshBase || (nodes[i].i<0 && -int64_t(nodes[i].i)>h.bvNodeCount-i)) return 0;
    for (int i=0;i<h.offMeshConCount;++i)
        if (connections[i].poly!=h.offMeshBase+i || !finite(connections[i].pos) || !finite(connections[i].pos+3) ||
            !std::isfinite(connections[i].rad) || connections[i].rad<0) return 0;
    auto copy = static_cast<unsigned char*>(dtAlloc(size, DT_ALLOC_PERM));
    if (!copy) return 0;
    std::memcpy(copy, data, size);
    auto status = static_cast<Context*>(context)->mesh.addTile(copy, size, DT_TILE_FREE_DATA, 0, nullptr);
    if (!ok(status)) { dtFree(copy); return 0; }
    return 1;
}
// Returns point count, or a negative reason. Inputs/outputs use WoW XYZ; Detour uses YZX.
int hb_nav_path(void* context, const float* from, const float* to, float* output, int capacity, int maxHops) {
    try {
        if (!context || !from || !to || !output || capacity < 2 || maxHops < 1 || maxHops > 65535 || !finite(from) || !finite(to)) return -1;
        auto& c = *static_cast<Context*>(context);
        float start[3]={from[1],from[2],from[0]}, end[3]={to[1],to[2],to[0]};
        float extents[3]={1.5f,3.0f,1.5f}, a[3], b[3];
        dtQueryFilter filter; filter.setIncludeFlags(1); filter.setExcludeFlags(0x1e); // walkable ground only
        dtPolyRef first=0,last=0;
        if (!ok(c.query.findNearestPoly(start,extents,&filter,&first,a)) || !first ||
            !ok(c.query.findNearestPoly(end,extents,&filter,&last,b)) || !last) return -2;
        auto near=[](const float* x,const float* y) { return std::hypot(x[0]-y[0],x[2]-y[2]) <= 0.75f && std::abs(x[1]-y[1]) <= 2.0f; };
        if (!near(start,a) || !near(end,b)) return -2;
        std::vector<dtPolyRef> corridor(maxHops); int count=0;
        if (!ok(c.query.findPath(first,last,a,b,&filter,corridor.data(),&count,maxHops)) || count==0 || corridor[count-1]!=last) return -3;
        for (int i=0;i<count;++i) {
            const dtMeshTile* tile; const dtPoly* poly;
            if (!ok(c.mesh.getTileAndPolyByRef(corridor[i],&tile,&poly)) || poly->getType()!=DT_POLYTYPE_GROUND) return -4;
        }
        std::vector<float> straight(capacity*3); std::vector<unsigned char> flags(capacity); int n=0;
        if (!ok(c.query.findStraightPath(a,b,corridor.data(),count,straight.data(),flags.data(),nullptr,&n,capacity,DT_STRAIGHTPATH_ALL_CROSSINGS)) || n<1 || !(flags[n-1]&DT_STRAIGHTPATH_END)) return -3;
        // Sample ground height along each convex corridor segment using its detail mesh.
        int written=0;
        for (int i=0;i<n;++i) {
            const float* p=&straight[i*3]; const float* prev=i?&straight[(i-1)*3]:p;
            int steps=std::max(1,int(std::ceil(std::hypot(p[0]-prev[0],p[2]-prev[2])/1.0f)));
            for(int j=1;j<=steps;++j) {
                float t=float(j)/steps, v[3]={prev[0]+(p[0]-prev[0])*t,prev[1]+(p[1]-prev[1])*t,prev[2]+(p[2]-prev[2])*t};
                bool found=false; float best=1e9f,height=0;
                for(int k=0;k<count;++k) {
                    float projected[3];
                    if(ok(c.query.closestPointOnPoly(corridor[k],v,projected,nullptr)) &&
                       std::hypot(projected[0]-v[0],projected[2]-v[2]) <= 0.02f && std::abs(projected[1]-v[1])<best)
                    {best=std::abs(projected[1]-v[1]);height=projected[1];found=true;}
                }
                if(!found || best>3.0f) return -4;
                if(written>=capacity) return -3;
                output[written*3]=v[2]; output[written*3+1]=v[0]; output[written*3+2]=height; ++written;
            }
        }
        return written;
    } catch (...) { return -1; }
}
}
