const fs=require('fs'),path=require('path'),crypto=require('crypto');
const file=path.join(__dirname,'runtime-manifest.json'),doc=JSON.parse(fs.readFileSync(file));
const folder='FirstConnection-mira-v1/characters/student_mira/';
const source=JSON.parse(fs.readFileSync(path.join(__dirname,folder,'manifest.json')));
const animations={};
for(const [key,row]of Object.entries(source.animation.rows))animations[key]={...row,rectangles:source.frame_layout.rows[key]};
doc.actors['npc.mira']={image:folder+source.game_input,pivot:[.5,.0625],animations,sha256:crypto.createHash('sha256').update(fs.readFileSync(path.join(__dirname,folder,source.game_input))).digest('hex')};
fs.writeFileSync(file,JSON.stringify(doc,null,2));console.log('Bound explicit npc.mira procedural prototype asset');
