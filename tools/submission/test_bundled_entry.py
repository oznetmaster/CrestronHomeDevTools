# Copyright (c) 2026 Neil Colvin. MIT licensed.
# Portable synthetic fixtures only; no saved credentials or runtime replacement.
import pathlib,sys,unittest,importlib.util,contextlib,io,json,hashlib,tempfile,stat,types
from unittest.mock import patch
import bundled_entry as m
class BundledEntryTests(unittest.TestCase):
 def setUp(self):
  self.temporary=tempfile.TemporaryDirectory(prefix="bundled-entry-fixture-")
  self.root=pathlib.Path(self.temporary.name).resolve()
 def tearDown(self):self.temporary.cleanup()
 def parent(self):return pathlib.Path(tempfile.mkdtemp(prefix="private-",dir=self.root))
 def execute(self,args,run):
  o,e=io.StringIO(),io.StringIO()
  with patch.object(sys,"argv",["entry","runtime-check",*args]),patch.object(m,"run",run),contextlib.redirect_stdout(o),contextlib.redirect_stderr(e):
   code=m.main()
  return code,o.getvalue(),e.getvalue()
 def test_legacy(self):
  calls=[]
  result=self.execute([],lambda:calls.append(True) or 0)
  self.assertEqual(result,(0,"",""));self.assertEqual(calls,[True])
 def test_optional_success_has_no_failure_files(self):
  parent=self.parent();result=self.execute(["--private-diagnostics-directory",str(parent)],lambda:0)
  self.assertEqual(result,(0,"",""));self.assertEqual(list(parent.iterdir()),[])
 def test_nested_error_private_full_chain_hash_public_redacted(self):
  parent=self.parent()
  def failure():
   try:raise ValueError("PRIVATE_INNER_SENTINEL")
   except ValueError as inner:raise RuntimeError("PRIVATE_OUTER_SENTINEL") from inner
  result=self.execute(["--private-diagnostics-directory",str(parent)],failure)
  self.assertEqual(result[0],1);self.assertNotIn("SENTINEL",result[1]+result[2])
  folders=list(parent.iterdir());self.assertEqual(len(folders),1)
  receipt=json.loads((folders[0]/"result.json").read_text());trace=(folders[0]/"exception.original.txt").read_bytes()
  self.assertIn(b"PRIVATE_INNER_SENTINEL",trace);self.assertIn(b"PRIVATE_OUTER_SENTINEL",trace)
  self.assertEqual(receipt["trace"]["sha256"],hashlib.sha256(trace).hexdigest());self.assertEqual(receipt["trace"]["length"],len(trace));self.assertFalse(receipt["validationPassed"])
 def test_write_denial_preserves_failure_without_fallback(self):
  parent=self.parent()
  with patch.object(m,"_retain_runtime_failure",side_effect=PermissionError("PRIVATE_DENIAL_SENTINEL")):
   result=self.execute(["--private-diagnostics-directory",str(parent)],lambda:(_ for _ in ()).throw(RuntimeError("PRIVATE_COMMAND_SENTINEL")))
  self.assertEqual(result[0],1);self.assertNotIn("SENTINEL",result[1]+result[2]);self.assertIn("retention failed",result[2]);self.assertEqual(list(parent.iterdir()),[])
 def test_actual_trace_write_denial_preserves_primary_failure(self):
  parent=self.parent();original=pathlib.Path.open
  def denied(path,*args,**kwargs):
   if path.name=="exception.original.txt":raise PermissionError("PRIVATE_WRITE_DENIAL_SENTINEL")
   return original(path,*args,**kwargs)
  with patch.object(pathlib.Path,"open",denied):
   result=self.execute(["--private-diagnostics-directory",str(parent)],lambda:(_ for _ in ()).throw(RuntimeError("PRIVATE_PRIMARY_SENTINEL")))
  self.assertEqual(result[0],1);self.assertNotIn("SENTINEL",result[1]+result[2]);self.assertIn("retention failed",result[2])
  directories=list(parent.iterdir());self.assertEqual(len(directories),1);self.assertEqual(list(directories[0].iterdir()),[])
 def test_invalid_destinations_deny_before_imports(self):
  parent=self.parent();file=parent/"file";file.write_text("fixture")
  for name in ["relative",str(parent/"missing"),str(file)]:
   calls=[];result=self.execute(["--private-diagnostics-directory",name],lambda:calls.append(True) or 0)
   self.assertEqual(result[0],1);self.assertEqual(calls,[])
  self.assertEqual(list(parent.iterdir()),[file])
 def test_nonleaf_link_ancestor_denied_before_run_without_fallback(self):
  parent=self.parent();ancestor=parent.parent;original=pathlib.Path.lstat
  def info(node):
   if node==ancestor:return types.SimpleNamespace(st_mode=stat.S_IFLNK,st_file_attributes=0)
   return original(node)
  calls=[]
  with patch.object(pathlib.Path,"lstat",info):
   result=self.execute(["--private-diagnostics-directory",str(parent)],lambda:calls.append(True) or 0)
  self.assertEqual(result[0],1);self.assertEqual(calls,[]);self.assertEqual(list(parent.iterdir()),[])
 def test_simulated_symlink_ancestor_is_rejected(self):
  parent=self.parent();ancestor=parent.parent;visited=[]
  def info(node):
   visited.append(node)
   return types.SimpleNamespace(st_mode=stat.S_IFLNK if node==ancestor else stat.S_IFDIR,st_file_attributes=0)
  with patch.object(pathlib.Path,"lstat",info):
   with self.assertRaises(ValueError):m._require_ordinary_ancestors(parent)
  self.assertIn(ancestor,visited)
 def test_simulated_reparse_ancestor_is_rejected(self):
  parent=self.parent();ancestor=parent.parent
  def info(node):return types.SimpleNamespace(st_mode=stat.S_IFDIR,st_file_attributes=1024 if node==ancestor else 0)
  with patch.object(stat,"FILE_ATTRIBUTE_REPARSE_POINT",1024,create=True),patch.object(pathlib.Path,"lstat",info):
   with self.assertRaises(ValueError):m._require_ordinary_ancestors(parent)
 def test_ancestor_inspection_denial_fails_closed(self):
  parent=self.parent()
  with patch.object(pathlib.Path,"lstat",side_effect=PermissionError("private")):
   with self.assertRaises(PermissionError):m._require_ordinary_ancestors(parent)
 def test_unknown_option_rejected_before_run(self):
  calls=[];result=self.execute(["--other","unused"],lambda:calls.append(True) or 0)
  self.assertEqual(result[0],1);self.assertEqual(calls,[])
 def test_keyboard_interrupt_still_130(self):
  result=self.execute([],lambda:(_ for _ in ()).throw(KeyboardInterrupt()))
  self.assertEqual(result[0],130)
if __name__=="__main__":unittest.main(verbosity=2)
