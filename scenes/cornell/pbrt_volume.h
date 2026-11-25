// Closures of the "osl" medium of pbrt (Magi) that stdosl.h does not declare. The weight of each closure is a coefficient of the medium,
// per unit length of the scene: the shader computes the coefficients and multiplies the closure by them.
//
//   henyey_greenstein(g) * sigma_s    scattering with the Henyey-Greenstein phase function of anisotropy g
//   isotropic() * sigma_s             the same with g = 0
//   absorption() * sigma_a            absorption
//   emission() * Le                   emission: the medium emits sigma_a * Le (radiance Le where it absorbs, as the media of pbrt)
//
// stdosl.h declares anisotropic_vdf(albedo, extinction, g) and medium_vdf(albedo, transmission_depth, transmission_color, g, ior, priority)
// with the definitions of testrender: sigma_t = extinction (or -ln(transmission_color) / transmission_depth), sigma_s = albedo * sigma_t.
#ifndef PBRT_VOLUME_H
#define PBRT_VOLUME_H
// (BUILTIN is spelled out: it is a macro of stdosl.h)


closure color henyey_greenstein(float g) [[ int builtin = 1 ]];
closure color isotropic() [[ int builtin = 1 ]];
closure color absorption() [[ int builtin = 1 ]];

#endif
