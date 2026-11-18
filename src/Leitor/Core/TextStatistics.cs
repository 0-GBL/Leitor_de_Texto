namespace Leitor.Core;

// Editor offsets use UTF-16. Only the affected chunks are rescanned during small edits.
public sealed class TextStatistics
{
    private sealed record Chunk(string Text,int Words,int Breaks,int Sentences);
    private readonly List<Chunk> chunks=[];
    public int Characters {get;private set;}
    public int Words {get;private set;}
    public int Lines {get;private set;}
    public int Sentences {get;private set;}
    public TextStatistics(string text=""){Replace(0,0,text);}
    private static Chunk Measure(string s)
    {
        int words=0,lines=0,sentences=0;bool word=false;
        for(int i=0;i<s.Length;i++){bool current=!char.IsWhiteSpace(s[i]);if(current&&!word)words++;word=current;if(s[i]=='\r'||s[i]=='\n'&&(i==0||s[i-1]!='\r'))lines++;if(".!?…".Contains(s[i])&&(i+1==s.Length||char.IsWhiteSpace(s[i+1])))sentences++;}return new(s,words,lines,sentences);
    }
    public void Replace(int offset,int removed,string inserted)
    {
        if(offset<0||removed<0||offset+removed>Characters)throw new ArgumentOutOfRangeException(nameof(offset));
        int first=0,start=0;while(first<chunks.Count&&start+chunks[first].Text.Length<offset){start+=chunks[first].Text.Length;first++;}
        int last=first,end=start;while(last<chunks.Count&&end+chunks[last].Text.Length<offset+removed){end+=chunks[last].Text.Length;last++;}
        string before=first<chunks.Count?chunks[first].Text[..(offset-start)]:"";
        string after=last<chunks.Count?chunks[last].Text[(offset+removed-end)..]:"";
        string changed=before+inserted+after;int take=last<chunks.Count?last-first+1:chunks.Count-first;if(take>0)chunks.RemoveRange(first,take);
        var replacement=new List<Chunk>();for(int p=0;p<changed.Length;p+=4096)replacement.Add(Measure(changed.Substring(p,Math.Min(4096,changed.Length-p))));chunks.InsertRange(first,replacement);
        Characters=Words=Lines=Sentences=0;char? previous=null;
        foreach(var chunk in chunks){Characters+=chunk.Text.Length;Words+=chunk.Words;Lines+=chunk.Breaks;Sentences+=chunk.Sentences;if(previous is char c){if(!char.IsWhiteSpace(c)&&!char.IsWhiteSpace(chunk.Text[0]))Words--;if(c=='\r'&&chunk.Text[0]=='\n')Lines--;}previous=chunk.Text[^1];}
        if(Characters>0)Lines++;
    }
    public double EstimatedSeconds(SpeechOptions options)=>Characters/(14*Math.Max(.1,options.Speed))+Math.Max(0,Lines-1)*options.ParagraphPause+Math.Max(0,Sentences-Math.Max(0,Lines-1))*options.SentencePause;
}