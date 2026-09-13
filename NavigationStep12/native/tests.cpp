#include "DetourNavMesh.h"
#include "DetourNavMeshBuilder.h"
#include "DetourAlloc.h"
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <limits>
#include <stdexcept>
#include <vector>

extern "C" {
void* hb_nav_create(const unsigned char*, int);
void hb_nav_destroy(void*);
int hb_nav_add_tile(void*, const unsigned char*, int);
int hb_nav_path(void*, const float*, const float*, float*, int, int);
}

int main() {
    void* context = nullptr;
    unsigned char* data = nullptr;
    int checks = 0;
    auto check = [&](bool pass, const char* reason) {
        ++checks;
        if (!pass) throw std::runtime_error(reason);
    };
    try {
        // Clockwise convex cells form an L, an isolated island and an upper floor.
        // No terrain files or game process are required for these geometric tests.
        const unsigned short vertices[] = {
            0,0,0, 0,0,4, 4,0,4, 10,0,4, 10,0,0,
            0,0,10, 4,0,10,
            20,0,0, 20,0,4, 24,0,4, 24,0,0,
            0,10,0, 0,10,4, 10,10,4, 10,10,0
        };
        const unsigned short cells[4][6] = {
            {0,1,2,3,4,0xffff}, {1,5,6,2,0xffff,0xffff},
            {7,8,9,10,0xffff,0xffff}, {11,12,13,14,0xffff,0xffff}
        };
        unsigned short polygons[48]; std::fill_n(polygons, 48, 0xffff);
        for (int i=0;i<4;++i) std::copy_n(cells[i],6,polygons+i*12);
        polygons[6+1] = 1;       // Lower cell edge 1 joins the upper arm.
        polygons[12+6+3] = 0;    // Reciprocal edge.
        unsigned short flags[4] = {1,1,1,1};
        unsigned char areas[4] = {0,0,0,0};
        dtNavMeshCreateParams create{};
        create.verts=vertices; create.vertCount=15; create.polys=polygons;
        create.polyFlags=flags; create.polyAreas=areas; create.polyCount=4; create.nvp=6;
        create.bmax[0]=32; create.bmax[1]=12; create.bmax[2]=32;
        create.cs=1; create.ch=1; create.walkableHeight=1.5f;
        create.walkableRadius=0.2f; create.walkableClimb=1.8f; create.buildBvTree=true;
        int size=0;
        check(dtCreateNavMeshData(&create,&data,&size), "fixture generation");
        dtNavMeshParams params{}; params.tileWidth=32; params.tileHeight=32; params.maxTiles=4;
        context=hb_nav_create(reinterpret_cast<unsigned char*>(&params),sizeof(params));
        check(context!=nullptr, "context initialization");
        check(hb_nav_add_tile(context,data,size-1)==0, "truncated tile accepted");
        auto* header=reinterpret_cast<dtMeshHeader*>(data);
        const int version=header->version;
        header->version=99;
        check(hb_nav_add_tile(context,data,size)==0, "wrong tile version accepted");
        header->version=version;
        auto* fixturePolys=reinterpret_cast<dtPoly*>(data+sizeof(dtMeshHeader)+15*12);
        const auto vertex=fixturePolys[0].verts[0];
        fixturePolys[0].verts[0]=60000;
        check(hb_nav_add_tile(context,data,size)==0, "out-of-bounds vertex accepted");
        fixturePolys[0].verts[0]=vertex;
        check(hb_nav_add_tile(context,data,size)==1, "fixture loading");
        check(hb_nav_add_tile(context,data,size)==0, "duplicate tile accepted");
        std::vector<float> out(4096*3);
        float start[3]={2,8,0}, end[3]={8,2,0}; // WoW XYZ -> Detour YZX.
        check(hb_nav_path(context,start,start,out.data(),4096,100)>0, "same point has no valid path");
        int n=hb_nav_path(context,start,end,out.data(),4096,100);
        check(n>2, "L-shaped path missing");
        double length=0;
        for (int i=0;i<n;++i) {
            check(!(out[i*3]>4.02f && out[i*3+1]>4.02f), "path cuts across L obstacle");
            check(std::abs(out[i*3+2])<0.01f, "wrong surface height");
            if(i) length+=std::hypot(out[i*3]-out[(i-1)*3],out[i*3+1]-out[(i-1)*3+1]);
        }
        check(length>std::hypot(end[0]-start[0],end[1]-start[1])+0.1, "path is an unchecked direct line");
        check(hb_nav_path(context,start,end,out.data(),4096,1)<0, "partial corridor accepted");
        check(hb_nav_path(context,start,end,out.data(),2,100)<0, "truncated output accepted");
        float island[3]={2,22,0}, upper[3]={2,8,10}, missing[3]={100,100,0};
        check(hb_nav_path(context,start,island,out.data(),4096,100)<0, "disconnected island accepted");
        check(hb_nav_path(context,start,upper,out.data(),4096,100)<0, "different floor treated as arrival");
        check(hb_nav_path(context,start,missing,out.data(),4096,100)<0, "off-mesh destination accepted");
        float nan[3]={std::numeric_limits<float>::quiet_NaN(),0,0};
        check(hb_nav_path(context,nan,end,out.data(),4096,100)<0, "NaN accepted");
        std::printf("Native navigation: PASS (%d checks; generated L corridor, island, upper floor)\n",checks);
        dtFree(data); hb_nav_destroy(context);
        return 0;
    } catch(const std::exception& e) {
        std::fprintf(stderr,"Native navigation: FAIL after %d checks: %s\n",checks,e.what());
        dtFree(data); hb_nav_destroy(context);
        return 1;
    }
}
