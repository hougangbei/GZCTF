import { expect, test, type Page } from '@playwright/test'
const contents = [{kind:'lesson',contentId:'one',title:'编码原理',summary:'理解编码与转义'}, {kind:'challenge',contentId:'two',title:'编码实战'}]
const skill = {skillId:'skill',name:'编码技能',summary:'从理论到实践',iconKey:'web',sortOrder:0,challengeCount:1,lessonCount:1,contents}
const tree = {skillTreeId:'tree',name:'Web 基础',summary:'安全学习路线',iconKey:'web',categoryCount:1,skillCount:1,lessonCount:1,challengeCount:1,categories:[{categoryId:'category',name:'基础类别',skills:[skill],contents}]}
async function setup(page:Page, anonymous=false) {
  await page.addInitScript(()=>localStorage.setItem('language',JSON.stringify('zh-CN')))
  await page.route('**/api/**',route=>route.fulfill({json:[]}))
  await page.route('**/api/account/profile',route=>route.fulfill({status:anonymous?401:200,json:anonymous?{}:{userId:'user',userName:'User',role:'User'}}))
  await page.route('**/api/config',route=>route.fulfill({json:{title:'NYNUCTF'}}))
  await page.route('**/api/skill-trees',route=>route.fulfill({json:[tree]}))
  await page.route('**/api/skill-trees/tree',route=>route.fulfill({json:tree}))
  await page.route('**/api/my-learning/skill-trees/tree',route=>route.fulfill({json:{totalCount:2,completedCount:1,completedLessonIds:['one'],solvedChallengeIds:[],categories:[{categoryId:'category',totalCount:2,completedCount:1}],skills:[{skillId:'skill',totalCount:2,completedCount:1}]}}))
  await page.route('**/api/learning-lessons/one?*',route=>route.fulfill({json:{lessonId:'one',title:'编码原理',body:'## 编码\n\n示例内容'}}))
}
test('three card levels lead to workspace, reader and legacy redirect',async({page})=>{
  await setup(page); await page.goto('/skill-trees')
  await page.getByRole('link',{name:/Web 基础/}).click()
  await page.getByRole('link',{name:/基础类别.*1 个技能/}).click()
  await expect(page).toHaveURL(/category\/skills$/)
  await page.getByRole('link',{name:/编码技能.*2 项内容/}).click()
  await expect(page.getByRole('navigation',{name:'课程目录'})).toBeVisible()
  await expect(page.getByText('1 / 2').first()).toBeVisible()
  await page.getByRole('region',{name:'技能内容'}).getByRole('link',{name:/编码原理/}).click()
  await expect(page.locator('article h1')).toHaveText('编码原理')
  await page.reload(); await expect(page.locator('article h1')).toHaveText('编码原理')
  await page.goto('/skill-trees/tree/category/lesson/one'); await expect(page).toHaveURL(/category\/skills\/skill\/lesson\/one$/)
  await page.goto('/skill-trees/tree?category=category'); await expect(page).toHaveURL(/category\/skills$/)
})
test('wrong parent context cannot open content',async({page})=>{
  await setup(page); await page.goto('/skill-trees/tree/wrong/skills/skill/lesson/one'); await expect(page).toHaveURL(/404/)
})
test('anonymous does not request personal progress',async({page})=>{
  await setup(page,true); const requests:string[]=[]
  page.on('request',request=>{if(request.url().includes('/api/my-learning/skill-trees/'))requests.push(request.url())})
  await page.goto('/skill-trees/tree/category/skills/skill'); await expect(page.getByRole('heading',{name:'编码技能',exact:true})).toBeVisible(); expect(requests).toEqual([])
})
test('mobile directory and reduced motion work',async({page})=>{
  await setup(page); await page.setViewportSize({width:320,height:844}); await page.emulateMedia({reducedMotion:'reduce'})
  await page.goto('/skill-trees/tree/category/skills/skill'); await page.getByRole('button',{name:'课程目录'}).click()
  await expect(page.getByRole('dialog').getByRole('link',{name:/编码原理/})).toBeVisible()
  await page.keyboard.press('Escape'); await expect(page.getByRole('button',{name:'课程目录'})).toBeFocused()
  expect(await page.evaluate(()=>document.documentElement.scrollWidth-innerWidth)).toBeLessThanOrEqual(1)
  expect(await page.getByRole('region',{name:'技能内容'}).getByRole('link').first().evaluate(e=>getComputedStyle(e).animationName)).toBe('none')
})
test('mobile selection focuses the asynchronously loaded lesson heading',async({page})=>{
  await setup(page); await page.setViewportSize({width:390,height:844})
  await page.route('**/api/learning-lessons/one?*',async route=>{await new Promise(resolve=>setTimeout(resolve,300));await route.fulfill({json:{lessonId:'one',title:'编码原理',body:'内容'}})})
  await page.goto('/skill-trees/tree/category/skills/skill')
  await page.getByRole('button',{name:'课程目录'}).click()
  await page.getByRole('dialog').getByRole('link',{name:/编码原理/}).click()
  await expect(page.locator('article h1')).toBeFocused()
})
test('personal progress failure leaves the public directory usable',async({page})=>{
  await setup(page); await page.route('**/api/my-learning/skill-trees/tree',route=>route.fulfill({status:503,json:{}}))
  await page.goto('/skill-trees/tree/category/skills/skill')
  await expect(page.getByRole('alert').filter({hasText:'个人进度加载失败'})).toBeVisible()
  await expect(page.getByRole('region',{name:'技能内容'}).getByRole('link',{name:/编码原理/})).toBeVisible()
})
test('accepted flag refreshes shared skill and tree progress',async({page})=>{
  await setup(page)
  let solved=false
  await page.route('**/api/my-learning/skill-trees/tree',route=>route.fulfill({json:{totalCount:2,completedCount:solved?2:1,completedLessonIds:['one'],solvedChallengeIds:solved?['two']:[],categories:[{categoryId:'category',totalCount:2,completedCount:solved?2:1}],skills:[{skillId:'skill',totalCount:2,completedCount:solved?2:1}]}}))
  await page.route('**/api/challenges/two?*',route=>route.fulfill({json:{id:'two',title:'编码实战',body:'找到 Flag',type:'StaticAttachment',ctfCategory:'Web',hasContainer:false,hintLocaleCount:0}}))
  await page.route('**/api/challenges/two/submissions',route=>{solved=true;return route.fulfill({json:{accepted:true,firstSolve:true,submissionId:'submission'}})})
  await page.goto('/skill-trees/tree/category/skills/skill/challenge/two')
  await page.getByRole('dialog').getByRole('textbox').first().fill('flag{test}')
  await page.getByRole('button',{name:'提交 Flag',exact:true}).click()
  await expect(page.getByRole('dialog').getByText('Flag 正确。')).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByText('2 / 2').first()).toBeVisible()
})
test('route, category and skill cards stay inside each responsive viewport',async({page},testInfo)=>{
  await setup(page)
  for(const width of [320,390,768,1024,1440]) {
    await page.setViewportSize({width,height:960})
    for(const path of ['/skill-trees','/skill-trees/tree','/skill-trees/tree/category/skills','/skill-trees/tree/category/skills/skill']) {
      await page.goto(path)
      await expect(page.locator('h1').first()).toBeVisible()
      expect(await page.evaluate(()=>document.documentElement.scrollWidth-innerWidth)).toBeLessThanOrEqual(1)
    }
    if(width===390 || width===1440) await page.screenshot({path:testInfo.outputPath(`skill-workspace-${width}.png`),fullPage:true})
  }
})
