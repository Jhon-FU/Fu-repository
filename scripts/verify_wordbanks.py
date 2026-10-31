from pathlib import Path
import json, hashlib
ROOT=Path(__file__).resolve().parents[1]
DATA=ROOT/'data'
def read(path):return json.loads(path.read_text('utf-8'))
summary=read(DATA/'catalog-summary.json')
assert len(summary)==5
excluded=read(DATA/'sources/school-exclusions.json')
school=set(excluded['schoolWords'])|set(excluded['basicDerivatives'])
evidence=read(DATA/'sources/exam-frequency.json')
banks={}
for item in summary:
 path=DATA/'recommended'/(item['id']+'.bank.json')
 assert hashlib.sha256(path.read_bytes()).hexdigest()==item['sha256']
 bank=read(path);words={w['word'] for w in bank['words']}
 assert len(words)==len(bank['words'])==item['count'] and not words&school
 assert all(w['meaning'].strip() for w in bank['words'])
 assert all(not w['hint'] and not w['example'] for w in bank['words'])
 for word in bank['words']:
  kind=word['level'].lower(); ev=evidence[kind].get(word['word'],{'months':[],'papers':[],'occurrences':0})
  assert word['examMonthCount']==len(ev['months']) and word['examPaperCount']==len(ev['papers'])
  if item['id'].endswith('-high'):
   assert len(ev['months'])>=3 and len({m[:4] for m in ev['months']})>=2 and ev['occurrences']>=5
 banks[item['id']]=words
assert not banks['cet4-pure']&banks['cet6-pure']
assert banks['cet4-high']<=banks['cet4-pure']
assert banks['cet6-high']<=banks['cet6-pure']
assert banks['cet46-high']==banks['cet4-high']|banks['cet6-high']
assert read(DATA/'sources/provenance.json')['personalProgressUsed'] is False
print('PASS: five bank hashes, membership, school exclusions, non-personal content and exact frequency thresholds')
