    public FfmpegAudioExtractionService(string? ffmpegPath = null)
        : this(new ProcessRunner(), ffmpegPath)
    {
    }

    internal FfmpegAudioExtractionService(IProcessRunner processRunner, string? ffmpegPath = null)
    {
        this.processRunner = processRunner;
        toolResolver = new FfmpegToolResolver(ffmpegPath);
    }