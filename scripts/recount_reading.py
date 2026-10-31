"""Recount public reading passages from locally supplied, audited HTML files.

Inputs must use YYYY-MM-N.html names. Raw exam articles are not distributed.
Only checked-in compact statistics in data/sources are written.
"""
from pathlib import Path
from html.parser import HTMLParser
import argparse, collections, hashlib, json, re

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--cet4-dir',type=Path,required=True)
parser.add_argument('--cet6-dir',type=Path,required=True)
args=parser.parse_args()
for folder in (args.cet4_dir,args.cet6_dir):
 if not folder.is_dir() or not list(folder.glob('*.html')):
  parser.error('Both input folders must contain exam HTML files.')
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'data/sources'
def read(path):return json.loads(path.read_text('utf-8-sig'))
def write(name,obj):(OUT/name).write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
lexicon={row['word']:row for row in read(OUT/'lexicon.json')}
excluded=read(OUT/'school-exclusions.json')
school=set(excluded['schoolWords'])
basic=set(excluded['basicDerivatives'])
lex=set(lexicon)|school|basic
owners=collections.defaultdict(set)
for word,row in lexicon.items():
 for item in row['exchange'].split('/'):
  key,_,value=item.partition(':')
  if key in {'p','d','i','3','s'}:
   for form in value.split(','):owners[form].add(word)
def lemma(token,nxt):
 if token=='according' and nxt=='to':return None
 if token=='united' and nxt in {'states','nations','kingdom'}:return None
 if token in lex:return token
 own=owners.get(token,set())
 return next(iter(own)) if len(own)==1 else token

class Node:
 def __init__(self,tag='',attrs=None):self.tag=tag;self.attrs=dict(attrs or []);self.children=[]
 def walk(self):
  yield self
  for child in self.children:
   if isinstance(child,Node):yield from child.walk()
 def text(self):
  result=''.join(x.text() if isinstance(x,Node) else x for x in self.children)
  return ' '+result+' ' if self.tag in {'div','p','li','h1','h2','h3','h4','br'} else result
class Parser(HTMLParser):
 void={'area','base','br','col','embed','hr','img','input','link','meta','param','source','track','wbr'}
 def __init__(self):super().__init__(convert_charrefs=True);self.root=Node('root');self.stack=[self.root]
 def handle_starttag(self,tag,attrs):
  n=Node(tag,attrs);self.stack[-1].children.append(n)
  if tag not in self.void:self.stack.append(n)
 def handle_startendtag(self,tag,attrs):self.stack[-1].children.append(Node(tag,attrs))
 def handle_endtag(self,tag):
  for i in range(len(self.stack)-1,0,-1):
   if self.stack[i].tag==tag:self.stack=self.stack[:i];break
 def handle_data(self,data):self.stack[-1].children.append(data)

statistics={};corpora=[]
for kind,folder in [('cet4',args.cet4_dir),('cet6',args.cet6_dir)]:
 ev=collections.defaultdict(lambda:{'months':set(),'papers':set(),'count':0,'forms':collections.Counter()})
 audit=[];seen=set();token_count=0
 for path in sorted(folder.glob('*.html')):
  parser=Parser();parser.feed(path.read_text('utf-8'))
  modules=[n for n in parser.root.walk() if n.tag=='div' and n.attrs.get('lang')=='en' and n.attrs.get('data-module-slug','').startswith('part3-')]
  row={'paper':path.stem,'url':f'https://english-exam.lazynote.cn/{kind}/paper/{path.stem}/','sha256':sha(path),'modules':[]}
  for mod in modules:
   # Pure reading passages only: exclude all questions, choices and explanations.
   paragraphs=[n for n in mod.walk() if n.attrs.get('id','').startswith('p-part3-')]
   text=' '.join(n.text() for n in paragraphs).lower().replace('’',"'")
   tokens=re.findall(r"[a-z]+(?:[-'][a-z]+)*",text)
   fingerprint=hashlib.sha256(' '.join(tokens).encode()).hexdigest()
   record={'section':mod.attrs['data-module-slug'],'tokens':len(tokens),'sha256Normalized':fingerprint}
   if len(tokens)<80:record['excluded']='fewer than 80 passage tokens'
   elif fingerprint in seen:record['excluded']='duplicate passage'
   else:
    seen.add(fingerprint);token_count+=len(tokens)
    for i,t in enumerate(tokens):
     word=lemma(t,tokens[i+1] if i+1<len(tokens) else '')
     if word not in lexicon:continue
     e=ev[word];e['months'].add(path.stem[:7]);e['papers'].add(path.stem);e['count']+=1;e['forms'][t]+=1
   row['modules'].append(record)
  audit.append(row)
 statistics[kind]={w:{'months':sorted(e['months']),'papers':sorted(e['papers']),'occurrences':e['count'],'forms':dict(e['forms'])} for w,e in sorted(ev.items())}
 corpora.append({'exam':kind,'downloadedPapers':len(audit),'papersWithReading':sum(any('excluded' not in m for m in a['modules']) for a in audit),
                 'acceptedPassages':len(seen),'tokens':token_count,'audit':audit})
 print(kind,'papers',len(audit),'passages',len(seen),'tokens',token_count,flush=True)

write('exam-frequency.json',statistics)
write('corpus-audit.json',corpora)
