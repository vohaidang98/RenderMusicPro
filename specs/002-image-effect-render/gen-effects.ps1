param([string]$Out, [string[]]$Only)
# Placeholder clips for pack 00_CoBan_Chung. Usage: .\gen-effects.ps1 -Out Assets\Presets_Effects\00_CoBan_Chung
# Generates 8 lightweight sample effects (black background, 1920x1080, 30 fps, 8 s, seamless where possible).
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Out | Out-Null
$H = 'mod(abs(sin(ld(0)*12.9898+ld(1)*78.233)*43758.5453),1)'   # hash of (ld0, ld1). NOTE: ffmpeg eval has only registers 0..9.
function Hash($a, $b) { "st(0,$a);st(1,$b);$H" }

$fx = [ordered]@{}

# 1. Film grain: random per-frame speckles + an occasional vertical scratch.
#    Shipped film_grain.mp4 is v2 (denser animated grain), made separately:
#    ffmpeg -f lavfi -i color=c=0x101010:s=960x540:r=24:d=2 -/vf grain2.txt -r 30 -c:v libx264 -preset slow -crf 35 film_grain.mp4
#    grain2.txt = noise=alls=60:allf=t+u,eq=brightness=-0.06,scale=1920:1080:flags=bicubic,geq=lum='clip(lum(X,Y)+<scratch+dust expr>,0,255)':cb=128:cr=128,format=yuv420p
$grain = "if(gt(random(1),0.9982),90+random(2)*120,0)+if(lt(abs(X-1920*mod(abs(sin(floor(T*6)*91.7)*4375.85),1)),1.5)*gt(mod(abs(sin(floor(T*6)*13.1)*977.3),1),0.55),70,0)"
$fx['film_grain'] = "geq=lum='$grain':cb=128:cr=128"

# 2. Twinkling stars: fixed positions on a 24px grid, brightness pulses (period divides 8 s).
$star = "st(2,floor(X/24));st(3,floor(Y/24));st(0,ld(2));st(1,ld(3));st(4,$H);st(0,ld(2)+7);st(5,$H);" +
        "st(6,hypot(mod(X,24)-12,mod(Y,24)-12));" +
        "if(gt(ld(4),0.965),255*clip(1-ld(6)/(1.5+3*ld(5)),0,1)*(0.55+0.45*sin(2*PI*(T/8*(1+floor(ld(5)*3))+ld(4)*7))),0)"
$fx['sparkle_stars'] = "geq=lum='$star':cb=128:cr=128"

# 3. Falling petals (pink ellipses, sway). Cell 120 px, 9 cells per 8 s loop -> 135 px/s.
$petalMask = "st(7,Y-T*135);st(8,X+18*sin(2*PI*T/8*2+floor(ld(7)/120)));st(2,floor(ld(8)/120));st(3,mod(floor(ld(7)/120),9));" +
        "st(0,ld(2));st(1,ld(3));st(4,$H);st(0,ld(2)+3);st(5,$H);" +
        "st(9,mod(ld(8),120)-60);st(0,mod(ld(7),120)-60);st(1,ld(5)*PI+2*PI*T/8*(1+floor(ld(4)*2)));" +
        "st(2,(ld(9)*cos(ld(1))+ld(0)*sin(ld(1)))/22);st(3,(-ld(9)*sin(ld(1))+ld(0)*cos(ld(1)))/11);" +
        "if(gt(ld(4),0.82),clip(1.4*(1-ld(2)*ld(2)-ld(3)*ld(3)),0,1),0)"
$fx['falling_petals'] = "format=gbrp,geq=r='255*($petalMask)':g='150*($petalMask)':b='190*($petalMask)'"

# 4. Simple bokeh: big soft circles that breathe (static positions, seamless pulse).
$bokehMask = "st(2,floor(X/240));st(3,floor(Y/240));st(0,ld(2));st(1,ld(3));st(4,$H);st(0,ld(2)+11);st(5,$H);" +
        "st(6,hypot(mod(X,240)-120-30*(ld(5)-0.5),mod(Y,240)-120-30*(ld(4)-0.5)));" +
        "if(gt(ld(4),0.45),pow(clip(1-ld(6)/(45+60*ld(5)),0,1),0.8)*(0.45+0.35*sin(2*PI*T/8+ld(5)*6)),0)"
$fx['bokeh_simple'] = "format=gbrp,geq=r='255*($bokehMask)':g='190*($bokehMask)':b='120*($bokehMask)'"

# 5. Falling dust: tiny dim golden specks drifting down. Cell 40, 8 cells per loop -> 40 px/s.
$dustMask = "st(7,Y-T*40);st(8,X+6*sin(2*PI*T/8+floor(ld(7)/40)));st(2,floor(ld(8)/40));st(3,mod(floor(ld(7)/40),8));" +
        "st(0,ld(2));st(1,ld(3));st(4,$H);st(0,ld(2)+5);st(5,$H);" +
        "st(6,hypot(mod(ld(8),40)-20,mod(ld(7),40)-20));if(gt(ld(4),0.9),0.7*clip(1-ld(6)/(1.2+2*ld(5)),0,1),0)"
$fx['falling_dust'] = "format=gbrp,geq=r='255*($dustMask)':g='215*($dustMask)':b='150*($dustMask)'"

# 6. Falling snow: white soft flakes. Cell 60, 9 cells per loop -> 67.5 px/s, gentle sway.
$snow = "st(7,Y-T*67.5);st(8,X+14*sin(2*PI*T/8*2+floor(ld(7)/60)));st(2,floor(ld(8)/60));st(3,mod(floor(ld(7)/60),18));" +
        "st(0,ld(2));st(1,ld(3));st(4,$H);st(0,ld(2)+9);st(5,$H);" +
        "st(6,hypot(mod(ld(8),60)-30,mod(ld(7),60)-30));if(gt(ld(4),0.86),255*clip(1.2-ld(6)/(2+5*ld(5)),0,1),0)"
$fx['snow_fall'] = "geq=lum='$snow':cb=128:cr=128"

# 7. Flying hearts: red/pink hearts rising. Cell 160, 8 cells per loop -> 160 px/s.
$heartMask = "st(7,Y+T*160);st(8,X+20*sin(2*PI*T/8*2+floor(ld(7)/160)));st(2,floor(ld(8)/160));st(3,mod(floor(ld(7)/160),8));" +
        "st(0,ld(2));st(1,ld(3));st(4,$H);st(0,ld(2)+13);st(5,$H);" +
        "st(2,(mod(ld(8),160)-80)/(28+18*ld(5)));st(3,-(mod(ld(7),160)-80)/(28+18*ld(5)));" +
        "if(gt(ld(4),0.78),lt(pow(ld(2)*ld(2)+ld(3)*ld(3)-1,3)-ld(2)*ld(2)*ld(3)*ld(3)*ld(3),0),0)"
$fx['flying_hearts'] = "format=gbrp,geq=r='255*($heartMask)':g='70*($heartMask)':b='120*($heartMask)'"


# 8. Disco lights: four coloured soft spotlights orbiting the frame (periods divide 8 s).
function Spot($k) { "pow(clip(1-hypot(X-960-620*cos(2*PI*T/8*$([int]$k+1)+$k*1.7),Y-540-330*sin(2*PI*T/8*$([int]$k+1)+$k*2.3))/260,0,1),1.4)" }
$s0 = Spot 0; $s1 = Spot 1; $s2 = Spot 2; $s3 = Spot 3
$fx['disco_lights'] = "format=gbrp,geq=r='255*clip($s0+$s2+0.6*$s3,0,1)':g='255*clip($s1+0.7*$s3,0,1)':b='255*clip($s2+$s1*0.4+$s0*0.8,0,1)'"
$jobs = foreach ($name in $fx.Keys) {
    if ($Only -and $Only -notcontains $name) { continue }
    $target = Join-Path $Out ($name + '.mp4')
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    $script = Join-Path $env:TEMP ("fx-" + ($name -replace '[/\\]', '_') + '.txt')
    [IO.File]::WriteAllText($script, $fx[$name] + ",format=yuv420p")
    Start-Job -ArgumentList $script, $target -ScriptBlock {
        param($script, $target)
        & ffmpeg -hide_banner -loglevel error -y -f lavfi -i "color=c=black:s=1920x1080:r=30:d=8" `
            "-/vf" $script -c:v libx264 -preset medium -crf 24 -pix_fmt yuv420p -movflags +faststart -an $target 2>&1
        "$target exit=$LASTEXITCODE"
    }
}
$jobs | Wait-Job | Receive-Job
