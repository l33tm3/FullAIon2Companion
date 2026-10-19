"""Translate only the new encyclopedia tables; preserve IDs, enums and numeric data."""
import sys,json,re
from pathlib import Path
sys.path.insert(0,str(Path('output/translation-runtime').resolve()))
import ctranslate2,sentencepiece
root=Path('public/data/game/encyclopedia')
cache=json.loads(Path('output/es-text-cache.json').read_text('utf8'))
cache.update(json.loads(Path('public/data/es-localization.json').read_text('utf8'))['dictionary'])
previous=Path('output/encyclopedia-es-texts.json')
if previous.exists():cache.update(json.loads(previous.read_text('utf8')))
exact={'Weapons':'Armas','Armor':'Armaduras','Accessories':'Accesorios','Arcana':'Arcana','Materials':'Materiales','Consumables':'Consumibles','Pets':'Mascotas','Other':'Otros','Blacksmithing':'Herrería','Handicrafting':'Artesanía','Armorsmithing':'Fabricación de armaduras','Alchemy':'Alquimia','Cooking':'Cocina','Conversion':'Conversión','Item Conversion':'Conversión de objetos','Monolith':'Monolito','Greatsword':'Mandoble','Longsword':'Espada','Dagger':'Daga','Bow':'Arco','Spellbook':'Libro de hechizos','Orb':'Orbe','Mace':'Maza','Staff':'Bastón','Fist':'Guanteletes','Guard':'Escudo auxiliar','Helm':'Casco','Pauldrons':'Hombreras','Top':'Peto','Legs':'Grebas','Gloves':'Guantes','Shoes':'Botas','Cloak':'Capa','Belt':'Cinturón','Earrings':'Pendientes','Necklace':'Collar','Ring':'Anillo','Bracelet':'Pulsera','Amulet':'Amuleto','Rune':'Runa','Bell':'Campana','Compass':'Brújula','Chalice':'Cáliz','Mirror':'Espejo','Parchment':'Pergamino','Wings Unlocking Item':'Desbloqueo de alas','Title Bestowing Item':'Desbloqueo de títulos','Crafting Material':'Material de fabricación','Gatherable Material':'Material recolectable','Special Material':'Material especial','Transmutation Material':'Material de transmutación','Substance Morph Material':'Material de conversión','Special Substance Morph Material':'Material especial de conversión','Upgrade':'Mejora','Empyrean Trace':'Vestigio empíreo','Substance Morph Design':'Diseño de conversión','Potion':'Poción','Food':'Comida','Beverage':'Bebida','Scroll':'Pergamino','Teleport':'Teletransporte','Manastone':'Piedra de maná','Theostone':'Piedra sagrada','Power Shard':'Fragmento de poder','Potential Enhance Stone':'Piedra de potencial','Transfer Stone':'Piedra de transferencia',"Philosopher's Stone":'Piedra filosofal','Pet':'Mascota','Soul Crystal':'Cristal de alma','Pet Transformation':'Transformación de mascota','Reward Chest':'Cofre de recompensa','Usage Count Recharge Ticket':'Recarga de entradas','Pantheon Decor':'Decoración del Panteón','Battle Pass':'Pase de batalla','Quest Scroll':'Pergamino de misión','Transformation':'Transformación','Special Points':'Puntos especiales','Miscellaneous':'Varios','Usage Duration Recharge Ticket':'Recarga de duración','Crafting Skill Learning Tool':'Herramienta de aprendizaje de fabricación','Emote':'Gesto','Valuables':'Objetos valiosos','Customization':'Personalización','Instant Clear Ticket':'Entrada de finalización instantánea','Attack':'Ataque','Critical Hit':'Golpe crítico','Critical Hit Resist':'Resistencia a críticos','Defense':'Defensa','Accuracy':'Precisión','Max HP':'PV máximos','Max MP':'PM máximos','Special':'Especial','Attack Bonus':'Bonificación de ataque','Accuracy Bonus':'Bonificación de precisión','Progress':'Progreso','Challenge':'Desafío','Exploration':'Exploración','Quest':'Misión','Dungeon':'Mazmorra','Resource':'Recurso'}
cache.update(exact)
fields={'name','description','categoryName','tab','otherSources','bonuses'}
files=[p for p in root.glob('*.json') if p.name!='assets-manifest.json']
data={p:json.loads(p.read_text('utf8')) for p in files}
# Category tuples keep their machine code and count intact.
index=data[root/'index.json']
for g in index['groups']:
 g['name']=exact.get(g['name'],cache.get(g['name'],g['name']))
 for c in g['cats']:c[1]=exact.get(c[1],cache.get(c[1],c[1]))
texts=set()
def collect(x,k=''):
 if isinstance(x,dict):
  for key,v in x.items():collect(v,key)
 elif isinstance(x,list):
  for v in x:collect(v,k)
 elif isinstance(x,str) and k in fields and re.search('[a-zA-Z]',x) and not x.startswith(('http','/')):texts.add(x)
for x in data.values():collect(x)
official=json.loads(Path('research/current/item_names_i18n.json').read_text('utf8'))
cache.update({name:v['es'] for name,v in official.items() if v.get('es')})
for path in ['research/current/npc_names.json','research/current/skill_names_i18n.json']:
 for v in json.loads(Path(path).read_text('utf8')).values():
  if v.get('en') and v.get('es'):cache[v['en']]=v['es']
already_spanish=set(cache.values())
resources=data.get(root/'resources.json',{})
already_spanish.update(x['name'] for x in resources.get('servers',{}).get('servers',[]))
already_spanish.update(x['name'] for x in resources.get('guilds',[]))
for p in ['public/data/game/catalog.json','public/data/catalog.json']:
 already_spanish.update(x['name'] for x in json.loads(Path(p).read_text('utf8'))['entries'] if x.get('originalName')!=x['name'] and x['name'] not in cache)
untranslated={x['name'] for x in index['entries'] if x.get('originalName')==x['name'] and ' ' in x['name']}
for t in untranslated:
 if cache.get(t)==t:del cache[t]
already_spanish.difference_update(untranslated)
missing=sorted(t for t in texts if t not in cache and t not in already_spanish)
model=Path('output/translation-model/en_es')
engine=ctranslate2.Translator(str(model/'model'),device='cpu',compute_type='int8',inter_threads=2,intra_threads=4)
sp=sentencepiece.SentencePieceProcessor(model_file=str(model/'sentencepiece.model'))
print('New texts',len(missing),flush=True)
for offset in range(0,len(missing),32):
 batch=missing[offset:offset+32]
 out=engine.translate_batch([sp.encode(t,out_type=str) for t in batch],beam_size=2,max_decoding_length=256)
 for t,r in zip(batch,out):cache[t]=sp.decode(r.hypotheses[0]).replace('(Bound)','(Vinculado)')
 if offset%160==0:print('Translated',offset,'/',len(missing),flush=True)
def convert(x,k=''):
 if isinstance(x,dict):return {key:convert(v,key) for key,v in x.items()}
 if isinstance(x,list):return [convert(v,k) for v in x]
 return cache.get(x,x) if isinstance(x,str) and k in fields else x
for p,x in data.items():p.write_text(json.dumps(convert(x),ensure_ascii=False,separators=(',',':')),'utf8')
Path('output/encyclopedia-es-texts.json').write_text(json.dumps({t:cache.get(t,t) for t in texts},ensure_ascii=False),'utf8')
print('Spanish encyclopedia saved',flush=True)
