using System; using System.Drawing; using System.Drawing.Imaging; using System.Collections.Generic; using System.Text;
// Converts a drawn level (blue outlined shapes = solid, red = spikes, blue frame + black floor line = bounds,
// a black 'S' label = player start) into the Redline tile text format.
public static class MapConvF {
  static int W,H,stride; static byte[] buf;
  static int Cls(int x,int y){ int i=y*stride+x*3; int b=buf[i],g=buf[i+1],r=buf[i+2];
    if (b>150 && r<120 && g<120) return 1; if (r>190 && g<90 && b<90) return 2; if (r<70 && g<70 && b<70) return 3; return 0; }
  static bool[] Filled(int color, int x0, int y0, int x1, int y1){
    int w=x1-x0+1, h=y1-y0+1; var wall=new bool[w*h]; var outside=new bool[w*h];
    for(int y=0;y<h;y++) for(int x=0;x<w;x++) wall[y*w+x]=Cls(x0+x,y0+y)==color;
    var st=new Stack<int>();
    for(int x=0;x<w;x++){ st.Push(x); st.Push((h-1)*w+x); } for(int y=0;y<h;y++){ st.Push(y*w); st.Push(y*w+w-1); }
    while(st.Count>0){ int p=st.Pop(); if(outside[p]||wall[p]) continue; outside[p]=true; int px=p%w, py=p/w;
      if(px>0) st.Push(p-1); if(px<w-1) st.Push(p+1); if(py>0) st.Push(p-w); if(py<h-1) st.Push(p+w); }
    var m=new bool[w*h]; for(int i=0;i<m.Length;i++) m[i]=!outside[i]; return m; }
  public static string Convert(string path, int tilesW, out string info) {
    var bmp = new Bitmap(path); W = bmp.Width; H = bmp.Height;
    var data = bmp.LockBits(new Rectangle(0,0,W,H), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
    stride = data.Stride; buf = new byte[stride*H];
    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length); bmp.UnlockBits(data); bmp.Dispose();
    // Bounds: a black frame when the drawing has one (its lines are the map edges and anything drawn inside,
    // blue border walls included, is part of the level); otherwise a blue frame on top/sides + black floor line.
    int top=-1, bottom=-1, left=-1, right=-1, frameColor=3;
    for(int y=0;y<H && top<0;y++){ int n=0; for(int x=0;x<W;x++) if(Cls(x,y)==3) n++; if(n>W*0.6) top=y; }
    for(int y=H-1;y>=0 && bottom<0;y--){ int n=0; for(int x=0;x<W;x++) if(Cls(x,y)==3) n++; if(n>W*0.6) bottom=y; }
    if (top >= 0 && bottom - top < 40) top = -1;   // only the floor line is black: blue frame drawing
    if (top < 0) { frameColor = 1; for(int y=0;y<H && top<0;y++){ int n=0; for(int x=0;x<W;x++) if(Cls(x,y)==1) n++; if(n>W*0.6) top=y; } }
    for(int x=0;x<W && left<0;x++){ int n=0; for(int y=0;y<H;y++) if(Cls(x,y)==frameColor) n++; if(n>(bottom-top)*0.8) left=x; }
    for(int x=W-1;x>=0 && right<0;x--){ int n=0; for(int y=0;y<H;y++) if(Cls(x,y)==frameColor) n++; if(n>(bottom-top)*0.8) right=x; }
    int inTop=top; for(int x=left+20;x<right-20;x+=7){ int y=top; while(y<H && Cls(x,y)==frameColor) y++; if(y>inTop && y-top<15) inTop=y; }
    int inLeft=left; for(int y=top+20;y<bottom-20;y+=7){ int x=left; while(x<W && Cls(x,y)==frameColor) x++; if(x>inLeft && x-left<15) inLeft=x; }
    int inRight=right; for(int y=top+20;y<bottom-20;y+=7){ int x=right; while(x>0 && Cls(x,y)==frameColor) x--; if(x<inRight && right-x<15) inRight=x; }
    int inBottom=bottom; while(Cls(W/2,inBottom)==3) inBottom--;
    int x0=inLeft+2, y0=inTop+2, x1=inRight-2, y1=inBottom-2, w=x1-x0+1, h=y1-y0+1;
    var blue=Filled(1,x0,y0,x1,y1); var red=Filled(2,x0,y0,x1,y1);
    double s=(right-left)/(double)tilesW; int tilesH=(int)Math.Round((bottom-top)/s);
    var cb=new double[tilesH,tilesW]; var cr=new double[tilesH,tilesW];
    for(int y=0;y<h;y++) for(int x=0;x<w;x++){
      int tx=(int)((x0+x-left)/s), tyb=(int)((bottom-(y0+y))/s); if(tx<0||tx>=tilesW||tyb<0||tyb>=tilesH) continue; int row=tilesH-1-tyb;
      if(blue[y*w+x]) cb[row,tx]++; if(red[y*w+x]) cr[row,tx]++; }
    double A=s*s; var grid=new char[tilesH,tilesW];
    for(int r=0;r<tilesH;r++) for(int c=0;c<tilesW;c++){ cb[r,c]/=A; cr[r,c]/=A; grid[r,c]='.'; }
    Func<int,int,double> B=(r,c)=> (r<0||c<0||r>=tilesH-1||c>=tilesW)?0:cb[r,c];
    Func<int,int,bool> S=(r,c)=> r>=0&&c>=0&&r<tilesH&&c<tilesW&&grid[r,c]=='#';
    for(int r=0;r<tilesH-1;r++) for(int c=0;c<tilesW;c++) if(cb[r,c]>=0.45) grid[r,c]='#';
    // Thin shapes straddling tiles: a horizontal run keeps its best row, a vertical run its best column.
    var add=new List<int[]>();
    for(int r=0;r<tilesH-1;r++) for(int c=0;c<tilesW;c++){ double v=cb[r,c]; if(v<0.12||grid[r,c]=='#') continue;
      bool solidUD = S(r-1,c) || S(r+1,c), solidLR = S(r,c-1) || S(r,c+1);
      bool horiz = (B(r,c-1)>=0.12 || S(r,c-1)) && (B(r,c+1)>=0.12 || S(r,c+1));
      bool vert = B(r-1,c)>=0.12 && B(r+1,c)>=0.12;
      if(horiz && !solidUD && v>=B(r-1,c) && v>=B(r+1,c)) add.Add(new[]{r,c});
      else if(vert && !solidUD && !solidLR && v>=B(r,c-1) && v>=B(r,c+1)) add.Add(new[]{r,c}); }
    foreach(var a in add) grid[a[0],a[1]]='#';
    for(int c=0;c<tilesW;c++) { grid[tilesH-1,c]='#'; grid[0,c]='.'; }
    for(int r=0;r<tilesH-1;r++) for(int c=0;c<tilesW;c++){ if(cr[r,c]<0.15) continue;
      if(grid[r,c]=='.') { grid[r,c]='^'; continue; } if(r>0 && grid[r-1,c]=='.') grid[r-1,c]='^'; }
    int sx=-1, sy=-1; for(int y=y0;y<y1-4 && sx<0;y++) for(int x=x0;x<x1;x++) if(Cls(x,y)==3){ sx=x; sy=y; break; }
    int pc=2, pr=tilesH-2;
    if(sx>=0){ pc=(int)((sx-left)/s); int tyb=Math.Max(1,(int)((bottom-sy)/s)); pr=tilesH-1-tyb; while(pr<tilesH-2 && grid[pr+1,pc]!='#') pr++; }
    grid[pr,pc]='P';
    info="frame "+left+","+top+"-"+right+","+bottom+" scale "+s.ToString("F2")+" grid "+tilesW+"x"+tilesH+" start "+pc+","+pr;
    var sb=new StringBuilder(); for(int r=0;r<tilesH;r++){ for(int c=0;c<tilesW;c++) sb.Append(grid[r,c]); sb.Append('\n'); } return sb.ToString();
  }
}
