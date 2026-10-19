from pathlib import Path
p=Path('src/scripts/app.ts');s=p.read_text(encoding='utf-8')
s=s.replace("for(const key of ['mine','target'] as const){const ref=storageRead('aion-ref-'+key,null);", "const earlyProfile=profileRoute();if(earlyProfile)void openProfile(earlyProfile);for(const key of earlyProfile?[]:['mine','target'] as const){const ref=storageRead('aion-ref-'+key,null);",1)
s=s.replace("const shared=profileRoute();if(shared)void openProfile(shared);else showView", "const shared=profileRoute();if(!shared)showView",1)
p.write_text(s,encoding='utf-8')
