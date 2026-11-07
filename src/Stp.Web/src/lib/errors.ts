import { ApiError } from '../api/client'

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isValidation && error.validationErrors.length > 0) {
      return error.validationErrors.join(' ')
    }
    return error.detail || error.title
  }

  if (error instanceof Error) return error.message
  return 'Đã xảy ra lỗi không xác định.'
}
