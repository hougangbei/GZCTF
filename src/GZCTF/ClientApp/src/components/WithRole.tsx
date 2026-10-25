import { Alert, Button, Center, Loader, Stack } from '@mantine/core'
import React, { FC, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import { useUser } from '@Hooks/useUser'
import { Role } from '@Api'

interface WithRoleProps {
  requiredRole: Role
  children?: React.ReactNode
}

export const RoleMap = new Map<Role, number>([
  [Role.Admin, 3],
  [Role.Monitor, 1],
  [Role.User, 0],
  [Role.Banned, -1],
])

export const RequireRole = (requiredRole: Role, role?: Role | null) =>
  RoleMap.get(role ?? Role.User)! >= RoleMap.get(requiredRole)!

export const WithRole: FC<WithRoleProps> = ({ requiredRole, children }) => {
  const { user, error, mutate } = useUser()
  const role = user?.role
  const { t } = useTranslation()
  const navigate = useNavigate()
  const location = useLocation()

  const required = RoleMap.get(requiredRole)!
  const current = role ? RoleMap.get(role) : undefined

  useEffect(() => {
    if (error && error.status === 401) {
      navigate(`/account/login?from=${location.pathname}`, { replace: true })
      return
    }

    if (!role) return

    if (current === undefined || current < required) navigate('/404', { replace: true })
  }, [role, current, error, required, navigate, location.pathname])

  if (error && error.status !== 401 && current === undefined) {
    return (
      <Center mih="16rem">
        <Stack align="center">
          <Alert color="red" role="alert">{t('common.error.encountered')}</Alert>
          <Button variant="light" onClick={() => { void mutate() }}>{t('common.button.try_again')}</Button>
        </Stack>
      </Center>
    )
  }

  if (current === undefined || current < required) {
    return (
      <Center h="calc(100vh - 32px)">
        <Loader />
      </Center>
    )
  }

  return <>{children}</>
}
