using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class MissionVoice : MonoBehaviour
{
    public enum Cue
    {
        None,
        WaitingToStart,
        ReachTerminal,
        TerminalInRange,
        Uploading,
        UploadComplete,
        ReachExtraction,
        MissionComplete
    }

    [Header("Voice Lines")]
    public AudioClip waitingToStart;
    public AudioClip reachTerminal;
    public AudioClip terminalInRange;
    public AudioClip uploading;
    public AudioClip uploadComplete;
    public AudioClip reachExtraction;
    public AudioClip missionComplete;

    [Header("Settings")]
    [Range(0f, 1f)]
    public float volume = 1f;

    private AudioSource source;
    private Cue lastCue = Cue.None;
    private bool terminalInRangePlayed;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
    }

    public void Play(Cue cue)
    {
        if (cue == lastCue)
            return;

        if (cue == Cue.TerminalInRange)
        {
            if (terminalInRangePlayed)
                return;

            terminalInRangePlayed = true;
        }

        // Do not repeat the search instruction after
        // the terminal has already been found.
        if (cue == Cue.ReachTerminal && terminalInRangePlayed)
            return;

        lastCue = cue;

        AudioClip clip = cue switch
        {
            Cue.WaitingToStart => waitingToStart,
            Cue.ReachTerminal => reachTerminal,
            Cue.TerminalInRange => terminalInRange,
            Cue.Uploading => uploading,
            Cue.UploadComplete => uploadComplete,
            Cue.ReachExtraction => reachExtraction,
            Cue.MissionComplete => missionComplete,
            _ => null
        };

        if (clip == null)
            return;

        source.Stop();
        source.volume = volume;
        source.clip = clip;
        source.Play();
    }
}