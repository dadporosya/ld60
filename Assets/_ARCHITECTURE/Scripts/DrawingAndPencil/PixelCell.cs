/// <summary>One pixel of the <see cref="PixelGrid"/>.</summary>
public class PixelCell
{
    /// <summary>0..1, see <see cref="SaturationSettings"/>.</summary>
    public float saturation;

    public SaturationZone zone = SaturationZone.Empty;

    /// <summary>
    /// How many separate times the pencil has worked this cell: the first touch, then +1 for every new
    /// stroke over it and every time the pencil crosses back over it after leaving it. Drives the rework
    /// speed-up (<see cref="SaturationSettings.ReworkFactor"/>).
    /// </summary>
    public int layers;

    /// <summary>Id of the last stroke that touched this cell.</summary>
    public int lastStrokeId = -1;

    /// <summary><c>Time.time</c> this cell was last touched.</summary>
    public float lastTouchTime = float.NegativeInfinity;

    /// <summary>
    /// Index into the stroke's path (<see cref="PixelGrid"/>'s brush centres) of the last touch, for
    /// spotting a stroke crossing its own line. Only meaningful while <see cref="lastStrokeId"/> is the current stroke.
    /// </summary>
    public int lastTouchStep;

    public void Reset()
    {
        saturation = 0f;
        zone = SaturationZone.Empty;
        layers = 0;
        lastStrokeId = -1;
        lastTouchTime = float.NegativeInfinity;
        lastTouchStep = 0;
    }
}
