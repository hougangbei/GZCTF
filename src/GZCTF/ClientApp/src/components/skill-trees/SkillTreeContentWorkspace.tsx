import { Center, Loader, Modal, Stack, Text } from '@mantine/core'
import { useTranslation } from 'react-i18next'
import { Navigate, useNavigate, useParams } from 'react-router'
import { WithNavBar } from '@Components/WithNavbar'
import { ChallengeWorkspace } from '@Components/learning/ChallengeWorkspace'
import { LessonWorkspace } from '@Components/learning/LessonWorkspace'
import { useSkillTree } from '@Hooks/useSkillTrees'
import classes from './SkillTreeContentWorkspace.module.css'
import { SkillTreeOutline } from './SkillTreeOutline'

export const SkillTreeContentWorkspace = () => {
  const { id: treeId, categoryId, kind, contentId } = useParams()
  const navigate = useNavigate()
  const { t } = useTranslation('skillTrees')
  const { data: tree, error } = useSkillTree(treeId)

  if (kind !== 'challenge' && kind !== 'lesson') return <Navigate to="/404" replace />

  if (!tree && !error) {
    return (
      <WithNavBar minWidth={0}>
        <Center h="60vh">
          <Loader />
        </Center>
      </WithNavBar>
    )
  }

  if (error || !tree) {
    return (
      <WithNavBar minWidth={0}>
        <Center h="60vh">
          <Text c="red">{t('errors.generic')}</Text>
        </Center>
      </WithNavBar>
    )
  }

  const category = tree.categories?.find((item) => item.categoryId === categoryId)
  const content = category?.contents?.find((item) => item.kind === kind && item.contentId === contentId)
  if (!category || !content) return <Navigate to="/404" replace />

  const closeHref = `/skill-trees/${treeId}?category=${categoryId}`
  return (
    <WithNavBar minWidth={0}>
      <Stack gap="lg">
        <SkillTreeOutline tree={tree} selectedCategoryId={categoryId} />
        <Modal
          opened
          onClose={() => navigate(closeHref)}
          title={content.title}
          size={kind === 'challenge' ? 860 : 'lg'}
          centered
          classNames={
            kind === 'challenge'
              ? { content: classes.content, header: classes.header, title: classes.title, body: classes.challengeBody }
              : { content: classes.content, body: classes.body }
          }
        >
          {kind === 'challenge' ? (
            <ChallengeWorkspace challengeId={contentId!} modal />
          ) : (
            <LessonWorkspace lessonId={contentId!} modal />
          )}
        </Modal>
      </Stack>
    </WithNavBar>
  )
}
