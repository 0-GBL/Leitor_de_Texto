namespace Leitor.Core;

// O(log n) prefix updates/queries keep the 50 ms UI tick independent of document length.
public sealed class PlaybackTimeline
{
    private double[] tree = [0];
    public void Reset(IReadOnlyList<double> durations)
    {
        tree = new double[durations.Count + 1];
        for (int i = 1; i < tree.Length; i++)
        {
            tree[i] += durations[i - 1]; int parent = i + (i & -i);
            if (parent < tree.Length) tree[parent] += tree[i];
        }
    }
    public void Add(int index, double delta) { for (int i = index + 1; i < tree.Length; i += i & -i) tree[i] += delta; }
    public double Before(int index) { double sum = 0; for (int i = Math.Min(index, tree.Length - 1); i > 0; i -= i & -i) sum += tree[i]; return sum; }
    public double Total => Before(tree.Length - 1);
    public int Locate(double seconds)
    {
        int index = 0, step = 1;
        while (step < tree.Length / 2) step <<= 1;
        for (; step > 0; step >>= 1)
        {
            int next = index + step;
            if (next < tree.Length && tree[next] <= seconds) { seconds -= tree[next]; index = next; }
        }
        return Math.Clamp(index, 0, Math.Max(0, tree.Length - 2));
    }
}
