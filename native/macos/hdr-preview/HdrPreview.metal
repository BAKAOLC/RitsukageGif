#include <metal_stdlib>
using namespace metal;
struct Vertex { float4 position [[position]]; float2 uv; };
vertex Vertex vertexMain(uint id [[vertex_id]]) {
    constexpr float2 p[] = {float2(-1,1),float2(-1,-1),float2(1,1),float2(1,-1)};
    Vertex o; o.position=float4(p[id],0,1); o.uv=float2((p[id].x+1)*.5,(1-p[id].y)*.5); return o;
}
fragment half4 fragmentMain(Vertex v [[stage_in]], texture2d<half> image [[texture(0)]],
                            constant float4& mode [[buffer(0)]], texture2d<half> patch [[texture(1)]],
                            constant float4& rect [[buffer(1)]], constant float4& selection [[buffer(2)]],
                            constant float& patchMode [[buffer(3)]]) {
    constexpr sampler linearSampler(coord::normalized, address::clamp_to_edge, filter::linear);
    constexpr sampler nearestSampler(coord::normalized, address::clamp_to_edge, filter::nearest);
    half4 c=mode.z!=0 ? image.sample(linearSampler,v.uv) : image.sample(nearestSampler,v.uv);
    if (all(v.uv >= rect.xy) && all(v.uv < rect.xy+rect.zw) &&
        all(v.uv >= selection.xy) && all(v.uv < selection.xy+selection.zw)) {
        half4 p=mode.z!=0 ? patch.sample(linearSampler,(v.uv-rect.xy)/rect.zw)
                         : patch.sample(nearestSampler,(v.uv-rect.xy)/rect.zw);
        half a=patchMode==2 ? half(1) : p.a;
        half ba=mode.y==2 ? half(1) : c.a;
        c=half4(p.rgb*(patchMode==1 ? a : half(1)) + c.rgb*(mode.y==1 ? ba : half(1))*(1-a),1);
    }
    half alpha = mode.y == 2 ? half(1) : c.a;
    return half4(c.rgb * (mode.y == 1 ? alpha : half(1)) / half(mode.x),alpha);
}
