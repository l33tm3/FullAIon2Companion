"""Translate public game text locally; canonical IDs/enums and all numbers stay unchanged."""
import sys,json,re,time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
sys.path.insert(0,str(Path('output/translation-runtime').resolve()))
import ctranslate2,sentencepiece
model=Path('output/translation-model/en_es')
translator=ctranslate2.Translator(str(model/'model'),device='cpu',compute_type='int8',inter_threads=2,intra_threads=4)
sp=sentencepiece.SentencePieceProcessor(model_file=str(model/'sentencepiece.model'))
cachefile=Path('output/es-text-cache.json')
cache=json.loads(cachefile.read_text('utf-8')) if cachefile.exists() else {}
exact={'Refining Stone':'Piedra de refinamiento',"Expert’s Refining Stone":'Piedra de refinamiento de experto',"Expert\'s Refining Stone":'Piedra de refinamiento de experto','Crafting Material':'Material de fabricación','Common':'Común','Uncommon':'Inusual','Rare':'Raro','Legend':'Legendario','Unique':'Único','Epic':'Épico','Heroic':'Heroico','Normal':'Normal','Elite':'Élite','Reward Chest':'Cofre de recompensa','A material used in Crafting.':'Material utilizado en la fabricación.','Mad Watcher Bulagan':'Vigilante enloquecido Bulagan','Morheim Boss':'Jefe de Morheim','Dratona Supporter':'Auxiliar Dratona','Training Dummy':'Muñeco de entrenamiento','Shugo Festival':'Festival de Shugo','Asmodians':'Asmodianos','Elyos':'Elios','Assassin':'Asesino','Gladiator':'Gladiador','Ranger':'Explorador','Sorcerer':'Hechicero','Chanter':'Cantor','Cleric':'Clérigo','Templar':'Templario','Elementalist':'Espiritualista'}
# Official Spanish terms override automatic translations for equipped game records.
official={}
for file in ['public/data/mine.json','public/data/target.json']:
 c=json.loads(Path(file).read_text('utf-8'))
 for x in c['equipment']['equipment']['equipmentList']+c['equipment']['skill']['skillList']:
  if 'id' in x: official[str(x['id'])]=x['name']
en=json.loads(Path('research/current/enUI.json').read_text('utf-8'));es=json.loads(Path('research/current/esUI.json').read_text('utf-8'))
def align(a,b):
 if isinstance(a,dict) and isinstance(b,dict):
  for k,v in a.items():
   if k in b:align(v,b[k])
 elif isinstance(a,str) and isinstance(b,str) and a!=b:exact[a]=b
align(en,es)
for name in ['item_names_i18n','skill_names_i18n','npc_names']:
 data=json.loads(Path('research/current/'+name+'.json').read_text('utf-8'))
 for k,v in data.items():
  if isinstance(v,dict) and v.get('es'):
   if name=='item_names_i18n':exact[k]=v['es']
   else:
    official[k]=v['es']
    if v.get('en'):exact[v['en']]=v['es']
fields={'name','title','description','categoryName','gradeName','typeName','text','goals','category','region','diff','categoryDescription'}
files=list(Path('public/data/game').rglob('*.json'))+list(Path('public/data/atlas').glob('*.json'))+[Path('public/data/catalog.json')]
texts=set()
def collect(v,key=''):
 if isinstance(v,dict):
  for k,x in v.items():
   if k in fields or isinstance(x,(dict,list)):collect(x,k)
 elif isinstance(v,list):
  for x in v:collect(x,key)
 elif isinstance(v,str) and key in fields and re.search('[A-Za-z]',v) and not v.startswith(('/assets/','http')):texts.add(v)
with ThreadPoolExecutor(max_workers=24) as pool:
 data=list(pool.map(lambda p:json.loads(p.read_text('utf-8')),files))
for v in data:collect(v)
cache.update(exact)
pending=sorted(texts-cache.keys());print('Public text to translate',len(pending),flush=True)
start=time.time()
for offset in range(0,len(pending),64):
 batch=pending[offset:offset+64]
 result=translator.translate_batch([sp.encode(t,out_type=str) for t in batch],beam_size=1,max_batch_size=64,max_decoding_length=768)
 for source,r in zip(batch,result):
  target=sp.decode(r.hypotheses[0])
  # Reject changes to mechanics such as damage, levels or percentages.
  numbers=lambda s:sorted(re.findall(r'\d+(?:[.,]\d+)?',s))
  cache[source]=target if numbers(source)==numbers(target) else source
 cachefile.write_text(json.dumps(cache,ensure_ascii=False),'utf-8')
 if offset%512==0:print('Translated',offset+len(batch),'/',len(pending),'seconds',round(time.time()-start),flush=True)
def convert(v,key=''):
 if isinstance(v,dict):
  result={k:convert(x,k) if k in fields or isinstance(x,(dict,list)) else x for k,x in v.items()}
  if isinstance(v.get('name'),str):
   result['originalName']=v.get('originalName',v['name'])
   if str(v.get('id')) in official:result['name']=official[str(v['id'])]
  return result
 if isinstance(v,list):return [convert(x,key) for x in v]
 return cache.get(v,v) if isinstance(v,str) and key in fields else v
def save(pair):
 p,v=pair;p.write_text(json.dumps(convert(v),ensure_ascii=False,separators=(',',':')),'utf-8')
with ThreadPoolExecutor(max_workers=24) as pool:list(pool.map(save,zip(files,data)))
Path('public/data/es-localization.json').write_text(json.dumps({'language':'es','method':'Traducción local automática con correcciones y nombres oficiales NCSOFT','translatedTexts':len(cache),'at':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'dictionary':cache},ensure_ascii=False,separators=(',',':')),'utf-8')
print('Spanish catalog written',len(files),'files',flush=True)
