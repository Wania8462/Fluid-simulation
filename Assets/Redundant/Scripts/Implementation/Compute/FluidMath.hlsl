static float PI = 3.1415926;

float SmoothingKernelPoly6(float dist2, float smoothRad, float smoothRad2, float denom)
{
    if (dist2 > smoothRad)
        return 0;

    return 315 * pow(abs(smoothRad2 - dist2), 3) / denom;
}

float Distance(float4 pos1, float4 pos2)
{
    return sqrt(pow(abs(pos1.x - pos2.x), 2) + pow(abs(pos1.y - pos2.y), 2) + pow(abs(pos1.z - pos2.z), 2));
}

float Distance2(float4 pos1, float4 pos2)
{
    return pow(abs(pos1.x - pos2.x), 2) + pow(abs(pos1.y - pos2.y), 2) + pow(abs(pos1.z - pos2.z), 2);
}