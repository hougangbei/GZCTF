import { Anchor, Button, PasswordInput, Select, Text, TextInput } from '@mantine/core'
import { useInputState } from '@mantine/hooks'
import { showNotification, updateNotification } from '@mantine/notifications'
import { mdiCheck, mdiClose } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router'
import useSWR from 'swr'
import { AccountView } from '@Components/AccountView'
import { Captcha, useCaptchaRef } from '@Components/Captcha'
import { StrengthPasswordInput } from '@Components/StrengthPasswordInput'
import { encryptApiData } from '@Utils/Crypto'
import { tryGetClientError } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import api, { fetcher, RegisterStatus, type ActiveCohortResponse } from '@Api'
import misc from '@Styles/Misc.module.css'

const Register: FC = () => {
  const [pwd, setPwd] = useInputState('')
  const [retypedPwd, setRetypedPwd] = useInputState('')
  const [uname, setUname] = useInputState('')
  const [email, setEmail] = useInputState('')
  const [realName, setRealName] = useInputState('')
  const [stdNumber, setStdNumber] = useInputState('')
  const [cohortId, setCohortId] = useState<string | null>(null)
  const [disabled, setDisabled] = useState(false)
  const { data: cohorts, error: cohortsError, isLoading: cohortsLoading, mutate: reloadCohorts } =
    useSWR<ActiveCohortResponse[]>('/api/cohorts/active', fetcher)
  const { config } = useConfig()

  const navigate = useNavigate()
  const { captchaRef, getToken, cleanUp } = useCaptchaRef()

  const { t } = useTranslation()

  const RegisterStatusMap = new Map([
    [
      RegisterStatus.LoggedIn,
      {
        message: t('account.notification.register.logged_in'),
      },
    ],
    [
      RegisterStatus.AdminConfirmationRequired,
      {
        title: t('account.notification.register.request_sent.title'),
        message: t('account.notification.register.request_sent.message'),
      },
    ],
    [
      RegisterStatus.EmailConfirmationRequired,
      {
        title: t('common.email.sent.title'),
        message: t('common.email.sent.message'),
      },
    ],
    [undefined, undefined],
  ])

  usePageTitle(t('account.title.register'))

  const onRegister = async (event: React.SyntheticEvent) => {
    event.preventDefault()

    if (!cohortId || !cohorts?.some((cohort) => cohort.id === cohortId)) {
      showNotification({ color: 'red', message: t('account.cohort.required') })
      return
    }

    if (pwd !== retypedPwd) {
      showNotification({
        color: 'red',
        title: t('common.error.check_input'),
        message: t('account.password.not_match'),
        icon: <Icon path={mdiClose} size={1} />,
      })
      return
    }

    const { valid, token } = await getToken()

    if (!valid) {
      showNotification({
        color: 'orange',
        title: t('account.notification.captcha.not_valid'),
        message: t('common.error.try_later'),
        loading: true,
      })
      return
    }

    setDisabled(true)

    showNotification({
      color: 'orange',
      id: 'register-status',
      title: t('account.notification.captcha.request_sent.title'),
      message: t('account.notification.captcha.request_sent.message'),
      loading: true,
      autoClose: false,
    })

    try {
      const res = await api.account.accountRegister({
        userName: uname,
        password: await encryptApiData(t, pwd, config.apiPublicKey),
        email: email,
        realName: realName.trim(),
        stdNumber: stdNumber.trim(),
        cohortId,
        challenge: token,
      })
      const data = RegisterStatusMap.get(res.data.data)
      if (data) {
        updateNotification({
          id: 'register-status',
          color: 'teal',
          title: data.title,
          message: data.message,
          icon: <Icon path={mdiCheck} size={1} />,
          loading: false,
          autoClose: true,
        })
        cleanUp(true)

        if (res.data.data === RegisterStatus.LoggedIn) navigate('/')
        else if (res.data.data === RegisterStatus.EmailConfirmationRequired)
          navigate('/account/pending', { state: { email } })
        else navigate('/account/login')
      }
    } catch (err: any) {
      const { title, message } = tryGetClientError(err, t)
      const cohortUnavailable = err?.response?.data?.title === 'Please select an active cohort.'
      if (cohortUnavailable) void reloadCohorts()

      updateNotification({
        id: 'register-status',
        color: 'red',
        title,
        message: cohortUnavailable ? t('account.cohort.invalid') : message,
        icon: <Icon path={mdiClose} size={1} />,
        loading: false,
        autoClose: true,
      })
      cleanUp(false)
    } finally {
      setDisabled(false)
    }
  }

  return (
    <AccountView onSubmit={onRegister}>
      <TextInput
        required
        label={t('account.label.email')}
        type="email"
        placeholder="ctf@example.com"
        w="100%"
        value={email}
        disabled={disabled}
        onChange={(event) => setEmail(event.currentTarget.value)}
      />
      <TextInput
        required
        label={t('account.label.username')}
        type="text"
        placeholder="ctfer"
        w="100%"
        value={uname}
        disabled={disabled}
        onChange={(event) => setUname(event.currentTarget.value)}
      />
      <TextInput required label={t('account.label.real_name')} value={realName}
        disabled={disabled} onChange={(event) => setRealName(event.currentTarget.value)} />
      <TextInput required label={t('account.label.student_id')} value={stdNumber}
        disabled={disabled} onChange={(event) => setStdNumber(event.currentTarget.value)} />
      <Select
        required
        label={t('account.label.cohort')}
        placeholder={t('account.cohort.placeholder')}
        data={cohorts?.map((cohort) => ({ value: cohort.id, label: cohort.name })) ?? []}
        value={cohortId}
        onChange={setCohortId}
        disabled={disabled || cohortsLoading || Boolean(cohortsError) || !cohorts?.length}
        w="100%"
      />
      {cohortsError && (
        <Text size="sm" c="red">
          {t('account.cohort.load_failed')}{' '}
          <Anchor component="button" type="button" onClick={() => void reloadCohorts()}>
            {t('account.cohort.retry')}
          </Anchor>
        </Text>
      )}
      {!cohortsLoading && !cohortsError && cohorts?.length === 0 && (
        <Text size="sm" c="red">{t('account.cohort.empty')}</Text>
      )}
      <StrengthPasswordInput value={pwd} onChange={(event) => setPwd(event.currentTarget.value)} disabled={disabled} />
      <PasswordInput
        required
        label={t('account.label.password_retype')}
        value={retypedPwd}
        onChange={(event) => setRetypedPwd(event.currentTarget.value)}
        disabled={disabled}
        w="100%"
        error={pwd !== retypedPwd}
      />
      <Captcha action="register" ref={captchaRef} />
      <Anchor fz="xs" className={misc.alignSelfEnd} component={Link} to="/account/login">
        {t('account.anchor.login')}
      </Anchor>
      <Button type="submit" fullWidth disabled={disabled || cohortsLoading || Boolean(cohortsError) || !cohorts?.length || !cohortId}>
        {t('account.button.register')}
      </Button>
    </AccountView>
  )
}

export default Register
