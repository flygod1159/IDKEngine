#version 460 core

const vec3 positions[24] =
{
    // Back
    vec3(-0.5,  0.5, -0.5 ),
    vec3(-0.5, -0.5, -0.5 ),
    vec3( 0.5, -0.5, -0.5 ),
    vec3( 0.5,  0.5, -0.5 ),

    // Front
    vec3(-0.5,  0.5,  0.5 ),
    vec3(-0.5, -0.5,  0.5 ),
    vec3( 0.5, -0.5,  0.5 ),
    vec3( 0.5,  0.5,  0.5 ),

    // Left
    vec3(-0.5,  0.5,  0.5 ),
    vec3(-0.5,  0.5, -0.5 ),
    vec3(-0.5, -0.5, -0.5 ),
    vec3(-0.5, -0.5,  0.5 ),

    // Right
    vec3( 0.5,  0.5,  0.5 ),
    vec3( 0.5,  0.5, -0.5 ),
    vec3( 0.5, -0.5, -0.5 ),
    vec3( 0.5, -0.5,  0.5 ),

    // Up
    vec3(-0.5,  0.5, -0.5 ),
    vec3(-0.5,  0.5,  0.5 ),
    vec3( 0.5,  0.5,  0.5 ),
    vec3( 0.5,  0.5, -0.5 ),

    // Down
    vec3(-0.5, -0.5, -0.5 ),
    vec3(-0.5, -0.5,  0.5 ),
    vec3( 0.5, -0.5,  0.5 ),
    vec3( 0.5, -0.5, -0.5 )
};

layout(std140, binding = 0) uniform BasicDataUBO
{
    mat4 ProjView;
    mat4 View;
    mat4 InvView;
    vec3 ViewPos;
    int FrameCount;
    mat4 Projection;
    mat4 InvProjection;
    mat4 InvProjView;
    mat4 PrevProjView;
    float NearPlane;
    float FarPlane;
} basicDataUBO;

out InOutVars
{
    vec3 TexCoord;
} outData;

void main()
{
    outData.TexCoord = positions[gl_VertexID];
    mat4 viewNoTranslation = mat4(basicDataUBO.View[0], basicDataUBO.View[1], basicDataUBO.View[2], vec4(0.0, 0.0, 0.0, 1.0));
    gl_Position = (basicDataUBO.Projection * viewNoTranslation * vec4(outData.TexCoord, 1.0)).xyww;
}