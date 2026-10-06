import React, { FC } from 'react'
import { WithNavBar } from '@Components/WithNavbar'
import { WithRole } from '@Components/WithRole'
import { AdminTabProps, WithAdminTab } from '@Components/admin/WithAdminTab'
import { Role } from '@Api'

interface AdminPageProps extends AdminTabProps {
  minWidth?: number
}

export const AdminPage: FC<AdminPageProps> = ({ minWidth = 1080, ...props }) => {
  return (
    <WithNavBar width="90%" minWidth={minWidth}>
      <WithRole requiredRole={Role.Admin}>
        <WithAdminTab {...props} />
      </WithRole>
    </WithNavBar>
  )
}
