    public FfmpegMediaProbe(string? ffmpegPath = null, string? ffprobePath = null)
        : this(new ProcessRunner(), ffmpegPath, ffprobePath)
    {
    }

    internal FfmpegMediaProbe(IProcessRunner processRunner, string? ffmpegPath = null, string? ffprobePath = null)
    {
        this.processRunner = processRunner;
        toolResolver = new FfmpegToolResolver(ffmpegPath, ffprobePath);
    }