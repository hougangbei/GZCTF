import { expect, test, type Page } from '@playwright/test'
const contents=[{kind:'lesson',contentId:'one',title:'课节一',sortOrder:0},{kind:'challenge',contentId:'two',title:'题目二',sortOrder:1}]
async function setup(page:Page) {
  await page.addInitScript(()=>localStorage.setItem('language',JSON.stringify('zh-CN')))
  await page.route('**/api/**',route=>route.fulfill({json:[]}))
  await page.route('**/api/account/profile',route=>route.fulfill({json:{userId:'admin',userName:'Admin',role:'Admin'}}))
  await page.route('**/api/config',route=>route.fulfill({json:{title:'NYNUCTF'}}))
  const skill={skillId:'skill',name:'编码技能',summary:'技能简介',iconKey:'web',isEnabled:true,rowVersion:4,categories:[{categoryId:'category',name:'类别一'}],contents}
  await page.route('**/api/admin/skills',route=>route.fulfill({json:[skill]}))
  await page.route('**/api/admin/skills/skill',route=>route.fulfill({json:skill}))
  await page.route('**/api/admin/skill-trees',route=>route.fulfill({json:[{skillTreeId:'tree',name:'路线一',rowVersion:1}]}))
  await page.route('**/api/admin/skill-categories',route=>route.fulfill({json:[{categoryId:'category',name:'类别一',trees:[{skillTreeId:'tree'}]}]}))
  await page.route('**/api/admin/lessons?*',route=>route.fulfill({json:[{id:'one',title:'课节一'}]}))
  await page.route('**/api/admin/challenges?*',route=>route.fulfill({json:[{id:'two',title:'题目二'}]}))
  return skill
}
test('skill library filters and creates a reusable skill',async({page})=>{
  await setup(page); await page.goto('/admin/skills')
  await expect(page.getByText('编码技能',{exact:true})).toBeVisible()
  await page.getByRole('textbox',{name:'搜索技能'}).fill('不存在')
  await expect(page.getByText('编码技能',{exact:true})).toHaveCount(0)
  await page.getByRole('button',{name:'新建技能',exact:true}).click()
  const dialog=page.getByRole('dialog')
  await dialog.getByRole('textbox',{name:'名称',exact:true}).fill('新技能')
  let sent:unknown
  await page.route('**/api/admin/skills',route=>{ if(route.request().method()==='POST'){sent=route.request().postDataJSON();return route.fulfill({json:{skillId:'new'}})}return route.fulfill({json:[]}) })
  await dialog.getByRole('button',{name:'创建技能',exact:true}).click()
  await expect(page).toHaveURL(/admin\/skills\/new$/)
  expect(sent).toMatchObject({name:'新技能',isEnabled:true})
})
test('skill content reorder submits mixed references and preserves concurrency version',async({page})=>{
  const skill=await setup(page); let sent:unknown
  await page.route('**/api/admin/skills/skill/contents',route=>{sent=route.request().postDataJSON();return route.fulfill({json:{...skill,rowVersion:5}})})
  await page.goto('/admin/skills/skill')
  await page.getByRole('button',{name:'下移 课节一',exact:true}).click()
  await page.getByRole('button',{name:'保存内容顺序',exact:true}).click()
  await expect(page.getByText('保存成功',{exact:true})).toBeVisible()
  expect(sent).toEqual({rowVersion:4,contents:[{kind:'challenge',contentId:'two',sortOrder:0},{kind:'lesson',contentId:'one',sortOrder:1}]})
  await expect(page.getByRole('link',{name:'路线一',exact:true})).toBeVisible()
})
test('conflict keeps local content edits until explicit reload',async({page})=>{
  await setup(page); await page.route('**/api/admin/skills/skill/contents',route=>route.fulfill({status:409,json:{code:'skill_tree_revision_conflict'}}))
  await page.goto('/admin/skills/skill'); await page.getByRole('button',{name:'下移 课节一',exact:true}).click(); await page.getByRole('button',{name:'保存内容顺序',exact:true}).click()
  await expect(page.getByRole('alert').filter({hasText:'重新加载'})).toBeVisible()
  await expect(page.getByRole('button',{name:'上移 题目二',exact:true})).toBeDisabled()
})
test('clean category refresh keeps membership snapshot paired with its version',async({page})=>{
  const skill=await setup(page)
  let category={categoryId:'category',name:'类别一',summary:'',iconKey:'web',rowVersion:4,trees:[],skills:[{...skill,sortOrder:0}]}
  let sent:unknown
  await page.route('**/api/admin/skill-categories/category',route=>route.fulfill({json:category}))
  await page.route('**/api/admin/skill-categories/category/skills',route=>{sent=route.request().postDataJSON();return route.fulfill({json:{...category,rowVersion:6}})})
  await page.goto('/admin/skill-categories/category')
  await expect(page.getByRole('link',{name:'编码技能',exact:true})).toBeVisible()
  category={...category,rowVersion:5,skills:[]}
  // A focus revalidation represents another administrator changing the clean form.
  await page.waitForTimeout(5100)
  await page.evaluate(()=>window.dispatchEvent(new Event('focus')))
  await expect(page.getByRole('link',{name:'编码技能',exact:true})).toHaveCount(0)
  await page.getByRole('button',{name:'保存技能关联与顺序',exact:true}).click()
  await expect.poll(()=>sent).toEqual({rowVersion:5,skills:[]})
})
