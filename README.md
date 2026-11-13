# TipoForSwarmUI

SwarmUI extension that adds a TIPO Prompt Generation parameter group. When the group is enabled, your prompt is upsampled with [TIPO](https://github.com/KohakuBlueleaf/z-tipo-extension) before text encoding, and the processed prompt is stored in image metadata.

Original work by [Hugs288](https://github.com/Hugs288). The original account was wiped (reason unknown). This is an updated fork, compatible with TIPO 2.x and current SwarmUI text encoders.

The 500m-ft TIPO model is recommended. Device defaults to CPU because CUDA sampling is not always reproducible.

## License

MIT. See [LICENSE](LICENSE). TIPO itself is by KohakuBlueleaf.
